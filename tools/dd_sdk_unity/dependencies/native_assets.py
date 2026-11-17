# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Pinned SDK release assets shared by development staging and release assembly."""

import shutil
import tempfile
from collections.abc import Callable
from pathlib import Path, PurePosixPath

from dd_sdk_unity.scripts.common.github import GitHubOperations, sha256
from dd_sdk_unity.scripts.common.log import get_default_logger
from dd_sdk_unity.scripts.common.versions.sdk_pin import NativeArtifact, SdkPin

from .native_metadata import write_metadata

REPO_ROOT = Path(__file__).resolve().parents[3]
PACKAGE_PATH = "packages/Datadog.Unity"
CACHE = REPO_ROOT / "build/native-assets"
SDKS: dict[str, dict[str, str]] = {
    "ios": {
        "repository": "DataDog/dd-sdk-ios",
        "pin": f"{PACKAGE_PATH}/Editor/iOS/IosDependencyVersion.json",
    },
    "cpp": {
        "repository": "DataDog/dd-sdk-cpp",
        "pin": f"{PACKAGE_PATH}/Editor/Desktop/DesktopDependencyVersion.json",
    },
}


def load(read: Callable[[str], str | bytes] | None = None) -> dict[str, NativeArtifact]:
    read = read or (lambda path: (REPO_ROOT / path).read_text())
    assets = {}
    for sdk in SDKS.values():
        pin = SdkPin.model_validate_json(read(sdk["pin"]))
        for platform, artifact in pin.artifacts.items():
            assets[platform] = NativeArtifact.model_validate(
                {
                    **artifact.model_dump(),
                    "name": artifact.name.format(version=pin.version),
                    "strip_prefix": artifact.strip_prefix.format(version=pin.version),
                    "platform": platform,
                    "repository": sdk["repository"],
                    "version": pin.version,
                }
            )
    return assets


def fetch(
    asset: NativeArtifact,
    cache: Path = CACHE,
    *,
    force: bool = False,
    allow_unknown_sha256: bool = False,
) -> Path:
    if not asset.sha256 and not allow_unknown_sha256:
        raise ValueError(f"No SHA-256 pinned for {asset.platform}")
    output = cache / asset.platform / asset.version / asset.name
    with GitHubOperations(asset.repository) as github:
        github.download_release_asset(asset.version, asset.name, output, asset.sha256, force=force)
    if not asset.sha256:
        asset.sha256 = sha256(output)
    return output


def verify(asset: NativeArtifact, package: Path) -> None:
    for name in asset.files:
        path = package / asset.destination / name
        if not path.exists() or (path.is_file() and not path.stat().st_size):
            raise ValueError(f"Missing or empty native asset: {path}")
    get_default_logger().info(f"Verified {asset.platform} SDK at {package / asset.destination}")


def stage(asset: NativeArtifact, archive: Path, package: Path) -> None:
    if sha256(archive) != asset.sha256:
        raise ValueError(f"Archive SHA-256 does not match the pin for {asset.platform}")
    destination = package / asset.destination
    get_default_logger().info(f"Staging {asset.platform} SDK at {destination}")
    with tempfile.TemporaryDirectory() as scratch:
        shutil.unpack_archive(archive, scratch, format="zip")
        for source in asset.files.values():
            path = Path(scratch) / asset.strip_prefix / source
            if not path.exists():
                raise ValueError(f"Missing release asset: {source}")
        _remove(destination)
        for name, source in asset.files.items():
            path = Path(scratch) / asset.strip_prefix / source
            target = destination / name
            get_default_logger().info(f"Copying {source} → {name}")
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.move(path, target)
    write_metadata(asset, package)


def _remove(path: Path) -> None:
    if path.is_dir():
        shutil.rmtree(path)
    else:
        path.unlink(missing_ok=True)
    Path(f"{path}.meta").unlink(missing_ok=True)


def clean(asset: NativeArtifact, package: Path, cache: Path = CACHE) -> None:
    get_default_logger().info(f"Removing {asset.platform} SDK at {package / asset.destination}")
    _remove(package / asset.destination)
    shutil.rmtree(cache / asset.platform, ignore_errors=True)


def is_staged(path: PurePosixPath, assets: dict[str, NativeArtifact]) -> bool:
    return any(path.is_relative_to(asset.destination) for asset in assets.values())


def ensure(*, force: bool = False) -> None:
    for asset in load().values():
        stage(asset, fetch(asset, force=force), REPO_ROOT / PACKAGE_PATH)
