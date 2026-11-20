# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Assemble and archive the pinned Unity package."""

import gzip
import json
import shutil
import tarfile
import tempfile
from pathlib import Path, PurePosixPath

from git.objects import Blob, Tree

from dd_sdk_unity.dependencies import native_assets
from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.log import get_default_logger
from dd_sdk_unity.scripts.common.versions.sdk_pin import SdkPin
from dd_sdk_unity.scripts.common.versions.semver import Version

from . import ANDROID_PIN_PATH, IOS_PIN_PATH, PACKAGE_PATH
from .models import AndroidPin

ADMINISTRATION = {".git", ".github", ".gitignore", ".gitattributes"}


def pins(git: GitOperations, sha: str) -> tuple[SdkPin, AndroidPin, SdkPin]:
    ios = SdkPin.model_validate_json(git.read(sha, IOS_PIN_PATH))
    android = AndroidPin(version=json.loads(git.read(sha, ANDROID_PIN_PATH))["version"])
    cpp = SdkPin.model_validate_json(git.read(sha, native_assets.SDKS["cpp"]["pin"]))
    return ios, android, cpp


def assemble(
    git: GitOperations, source_sha: str, archives: dict[str, Path], output: Path
) -> None:
    """Accept explicit paths; no checkout mutation, GitHub writes, or release records needed."""
    if output.exists():
        raise ValueError(f"Refusing to overwrite package output {output}")
    output.mkdir(parents=True)
    assets = native_assets.load(lambda path: git.read(source_sha, path))
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
        if native_assets.is_staged(relative, assets):
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
    for platform, asset in assets.items():
        native_assets.stage(asset, archives[platform], output)


def assemble_package(
    git: GitOperations, source_sha: str, archives: dict[str, Path], output: Path
) -> None:
    log = get_default_logger()
    if output.exists():
        raise ValueError(f"Assembly output {output} already exists")
    version = json.loads(git.read(source_sha, f"{PACKAGE_PATH}/package.json"))["version"]
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as scratch:
        payload = Path(scratch) / "package"
        assemble(git, source_sha, archives, payload)
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
    assembly = (payload / "Runtime/AssemblyInfo.cs").read_text()
    if f'AssemblyVersion("{Version.parse(version).core}")' not in assembly:
        raise ValueError("Assembled assembly version differs from the release plan")
    if f'AssemblyInformationalVersion("{version}")' not in assembly:
        raise ValueError("Assembled informational version differs from the release plan")
