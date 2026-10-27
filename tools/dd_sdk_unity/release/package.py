# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Assemble and archive the pinned Unity package."""

import gzip
import hashlib
import json
import plistlib
import shutil
import tarfile
import tempfile
import uuid
from pathlib import Path, PurePosixPath
from textwrap import dedent

import requests
from git.objects import Blob, Tree

from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.github import GitHubOperations
from dd_sdk_unity.scripts.common.log import get_default_logger

from . import ANDROID_PIN_PATH, IOS_PIN_PATH, PACKAGE_PATH
from .models import AndroidPin, IosPin

ADMINISTRATION = {".git", ".github", ".gitignore", ".gitattributes"}


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def pins(git: GitOperations, sha: str) -> tuple[IosPin, AndroidPin]:
    ios = IosPin.model_validate_json(git.read(sha, IOS_PIN_PATH))
    android = AndroidPin(version=json.loads(git.read(sha, ANDROID_PIN_PATH))["version"])
    return ios, android


def native_archive(pin: IosPin, output: Path) -> Path:
    log = get_default_logger()
    with GitHubOperations("DataDog/dd-sdk-ios") as github:
        url = github.get_release_asset_url(pin.version, "Datadog.xcframework.zip")
    log.info(f"Downloading native archive: {url}")
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix(".download")
    with requests.get(url, stream=True, timeout=180) as response:
        response.raise_for_status()
        with temporary.open("wb") as stream:
            for chunk in response.iter_content(chunk_size=1024 * 1024):
                stream.write(chunk)
    if sha256(temporary) != pin.sha256:
        temporary.unlink()
        raise ValueError(f"Native archive SHA-256 does not match pin {pin.version}")
    temporary.replace(output)
    return output


def _guid(name: str) -> str:
    return uuid.uuid5(uuid.NAMESPACE_URL, f"com.datadoghq.unity/{name}").hex


def extract_native(archive: Path, output: Path, pin: IosPin) -> None:
    if sha256(archive) != pin.sha256:
        raise ValueError("Native archive SHA-256 does not match the committed pin")
    with tempfile.TemporaryDirectory() as scratch:
        shutil.unpack_archive(archive, scratch)
        output.mkdir(parents=True, exist_ok=True)
        for module in pin.modules:
            framework = Path(scratch) / "Datadog.xcframework" / f"{module}.xcframework"
            info = framework / "Info.plist"
            if not info.is_file():
                raise ValueError(f"Missing native module {module}")
            with info.open("rb") as stream:
                libraries = plistlib.load(stream).get("AvailableLibraries", [])
            ios = [lib for lib in libraries if lib.get("SupportedPlatform") == "ios"]
            if not (
                any(lib.get("SupportedPlatformVariant") == "simulator" for lib in ios)
                and any(lib.get("SupportedPlatformVariant") != "simulator" for lib in ios)
            ):
                raise ValueError(f"{module} must contain iOS device and simulator libraries")
            shutil.move(framework, output / framework.name)


def assemble(git: GitOperations, source_sha: str, archive: Path, output: Path) -> None:
    """Accept explicit paths; no checkout mutation, GitHub writes, or release records needed."""
    if output.exists():
        raise ValueError(f"Refusing to overwrite package output {output}")
    output.mkdir(parents=True)
    tree = git.repository.commit(source_sha).tree
    package_tree = tree / PACKAGE_PATH
    if not isinstance(package_tree, Tree):
        raise TypeError("Source package path is not a Git tree")
    for blob in package_tree.traverse():
        if not isinstance(blob, Blob):
            continue
        relative = PurePosixPath(blob.path).relative_to(PACKAGE_PATH)
        if relative.parts[0].startswith(".git") or relative.parts[0] in {"Tests", "Tests.meta"}:
            continue
        # Native binaries come exclusively from the verified pin, never a source worktree.
        if any(part.endswith(".xcframework") for part in relative.parts):
            continue
        destination = output / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        data = blob.data_stream.read()
        if blob.mode == 0o120000:
            destination.symlink_to(data.decode())
        else:
            destination.write_bytes(data)
            destination.chmod(0o755 if blob.mode & 0o111 else 0o644)
    readme = output / "README.md"
    snippet = git.read(source_sha, "tools/snippets/deployment_repo.md").decode()
    readme.write_text(
        "".join(
            snippet if line.startswith("[//]: # (Repo Note)") else line
            for line in readme.read_text().splitlines(keepends=True)
        )
    )
    editor_assets = [
        p
        for p in (output / "Editor").iterdir()
        if p.name.casefold() == "com.datadoghq.unity.editor.asmdef"
    ]
    if len(editor_assets) == 1:
        editor = Path(f"{editor_assets[0]}.meta")
        aliases = [
            p for p in editor.parent.iterdir() if p.name.casefold() == editor.name.casefold()
        ]
        if aliases:
            temporary = editor.with_suffix(".meta.rename")
            aliases[0].rename(temporary)
            temporary.rename(editor)
    ios, _ = pins(git, source_sha)
    plugins = output / "Plugins/iOS"
    extract_native(archive, plugins, ios)
    for module in ios.modules:
        meta = plugins / f"{module}.xcframework.meta"
        if not meta.exists():
            # Stable plugin identity even when source intentionally ignores staged assets.
            meta.write_text(
                dedent(f"""\
                    fileFormatVersion: 2
                    guid: {_guid(module + '.xcframework')}
                    PluginImporter:
                      externalObjects: {{}}
                      serializedVersion: 2
                      iconMap: {{}}
                      executionOrder: {{}}
                      defineConstraints: []
                      isPreloaded: 0
                      isOverridable: 1
                      isExplicitlyReferenced: 0
                      validateReferences: 1
                      platformData:
                      - first:
                          Any:
                        second:
                          enabled: 0
                          settings: {{}}
                      - first:
                          Editor: Editor
                        second:
                          enabled: 0
                          settings:
                            DefaultValueInitialized: true
                      - first:
                          iPhone: iOS
                        second:
                          enabled: 1
                          settings:
                            AddToEmbeddedBinaries: true
                      userData:
                      assetBundleName:
                      assetBundleVariant:
                    """)
            )


def assemble_package(
    git: GitOperations, source_sha: str, archive: Path, output: Path
) -> None:
    log = get_default_logger()
    if output.exists():
        raise ValueError(f"Assembly output {output} already exists")
    version = json.loads(git.read(source_sha, f"{PACKAGE_PATH}/package.json"))["version"]
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as scratch:
        payload = Path(scratch) / "package"
        assemble(git, source_sha, archive, payload)
        check_versions(payload, version)
        archive_package(payload, output)
    log.info(f"Assembled Unity SDK {version} from {source_sha}: {output}")


def archive_package(payload: Path, output: Path) -> None:
    def normalize(info: tarfile.TarInfo) -> tarfile.TarInfo:
        info.uid = info.gid = info.mtime = 0
        info.uname = info.gname = ""
        return info

    with (
        output.open("wb") as raw,
        gzip.GzipFile(fileobj=raw, mode="wb", filename="", mtime=0) as gz,
        tarfile.open(fileobj=gz, mode="w") as tar,
    ):
        tar.add(payload, arcname=".", filter=normalize)


def extract_package(archive: Path, output: Path) -> None:
    with tarfile.open(archive, "r:gz") as tar:
        tar.extractall(output, filter="data")


def install(payload: Path, checkout: Path) -> None:
    for path in checkout.iterdir():
        if path.name in ADMINISTRATION:
            continue
        if path.is_dir() and not path.is_symlink():
            shutil.rmtree(path)
        else:
            path.unlink()
    if any(path.name in ADMINISTRATION for path in payload.iterdir()):
        raise ValueError("SDK payload cannot replace distribution administration files")
    shutil.copytree(payload, checkout, dirs_exist_ok=True, symlinks=True)


def check_versions(payload: Path, version: str) -> None:
    if json.loads((payload / "package.json").read_text())["version"] != version:
        raise ValueError("Assembled package version differs from the release plan")
    if f'AssemblyVersion("{version}")' not in (payload / "Runtime/AssemblyInfo.cs").read_text():
        raise ValueError("Assembled assembly version differs from the release plan")
