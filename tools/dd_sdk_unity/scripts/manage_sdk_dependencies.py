# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Stage, verify, clean, and upgrade native SDK dependencies."""

import argparse
import copy
import json
import shutil
import tempfile
import zipfile
from logging import Logger
from pathlib import Path

import requests

from dd_sdk_unity.dependencies import native_assets
from dd_sdk_unity.scripts import android_maven
from dd_sdk_unity.scripts.common.log import init_logger
from dd_sdk_unity.scripts.common.versions.android_deps import (
    ANDROID_DEPENDENCY_VERSION_RELPATH,
    read_android_dependency_pin,
)
from dd_sdk_unity.scripts.common.versions.sdk_pin import SdkPin
from dd_sdk_unity.scripts.common.versions.semver import Version


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    commands = result.add_subparsers(dest="command", required=True)
    for command in ("stage", "verify", "clean"):
        subparser = commands.add_parser(command)
        if command == "stage":
            subparser.add_argument("--force", action="store_true", help="Re-download all archives")
    upgrade = commands.add_parser("upgrade")
    upgrade.add_argument("--sdk", choices=(*native_assets.SDKS, "android"), required=True)
    upgrade.add_argument("--version", type=Version.parse, required=True)
    upgrade.add_argument(
        "--force", action="store_true", help="Recheck the currently pinned version"
    )
    upgrade.add_argument(
        "--dry-run", action="store_true", help="Validate without changing the pin or staged SDKs"
    )
    upgrade.add_argument(
        "--allow-transitive-drift",
        action="store_true",
        help="Accept reviewed Kotlin/OkHttp dependency changes (Android only)",
    )
    return result


def _write_pin(path: Path, document: dict) -> None:
    with tempfile.NamedTemporaryFile(
        "w", dir=path.parent, delete=False, encoding="utf-8"
    ) as stream:
        temporary = Path(stream.name)
        json.dump(document, stream, indent=2)
        stream.write("\n")
    try:
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def _publish_upgrade(staged: Path, package: Path, pin: Path, document: dict) -> None:
    destinations = [Path(artifact["destination"]) for artifact in document["artifacts"].values()]
    sidecars = {Path(f"{destination}.meta") for destination in destinations}
    paths = list(destinations)
    for meta in staged.rglob("*.meta"):
        relative = meta.relative_to(staged)
        if any(relative.is_relative_to(destination) for destination in destinations):
            continue
        if relative in sidecars or not (package / relative).exists():
            paths.append(relative)

    changed: list[tuple[Path, Path]] = []
    created: set[Path] = set()
    try:
        for relative in paths:
            target = package / relative
            backup = staged.parent / "backup" / relative
            for parent in target.parents:
                if parent.exists():
                    break
                created.add(parent)
            target.parent.mkdir(parents=True, exist_ok=True)
            if target.exists() or target.is_symlink():
                backup.parent.mkdir(parents=True, exist_ok=True)
                target.rename(backup)
            changed.append((target, backup))
            (staged / relative).rename(target)
        _write_pin(pin, document)
    except BaseException:
        for target, backup in reversed(changed):
            if target.is_dir() and not target.is_symlink():
                shutil.rmtree(target)
            else:
                target.unlink(missing_ok=True)
            if backup.exists() or backup.is_symlink():
                backup.rename(target)
        for parent in sorted(created, key=lambda path: len(path.parts), reverse=True):
            if parent.exists():
                parent.rmdir()
        raise


def _upgrade(args: argparse.Namespace, log: Logger) -> None:
    if args.allow_transitive_drift and args.sdk != "android":
        raise ValueError("--allow-transitive-drift applies only to Android")
    relative = (
        ANDROID_DEPENDENCY_VERSION_RELPATH
        if args.sdk == "android"
        else native_assets.SDKS[args.sdk]["pin"]
    )
    path = native_assets.REPO_ROOT / relative
    contents = path.read_text()
    document = json.loads(contents)
    if args.sdk == "android":
        current = read_android_dependency_pin(contents)
        current_version = str(current.version)
    else:
        current_version = SdkPin.model_validate_json(contents).version
    version = str(args.version)
    if current_version == version and not args.force:
        log.info(f"{args.sdk} SDK is already pinned to {version}.")
        return
    candidate = copy.deepcopy(document)
    candidate["version"] = version
    if args.sdk == "android":
        artifacts = current.artifacts
        unreachable = android_maven.verify_artifacts_reachable(log, artifacts, version)
        if unreachable:
            raise ValueError("Unreachable Maven artifacts: " + ", ".join(unreachable))
        observed = android_maven.collect_transitives(log, artifacts, version)
        drift = android_maven.check_transitive_drift(observed)
        if drift and not args.allow_transitive_drift:
            raise ValueError(
                "Transitive dependencies changed:\n"
                + "\n".join(drift)
                + "\nReview and rerun with --allow-transitive-drift."
            )
        for message in drift:
            log.warning(message)
        if not args.dry_run:
            _write_pin(path, candidate)
    else:
        for artifact in candidate["artifacts"].values():
            artifact["sha256"] = None

        with tempfile.TemporaryDirectory(dir=native_assets.REPO_ROOT) as scratch:
            package = Path(scratch) / "package"
            for platform, asset in native_assets.load(
                lambda _: json.dumps(candidate), sdk=args.sdk
            ).items():
                archive = native_assets.fetch(asset, force=True, allow_unknown_sha256=True)
                native_assets.stage(asset, archive, package)
                native_assets.verify(asset, package)
                candidate["artifacts"][platform]["sha256"] = asset.sha256
            if not args.dry_run:
                _publish_upgrade(
                    package, native_assets.REPO_ROOT / native_assets.PACKAGE_PATH, path, candidate
                )
    if args.dry_run:
        log.info(f"--dry-run: would write {relative}:\n{json.dumps(candidate, indent=2)}")
        return
    log.info(f"Updated {args.sdk} SDK: {current_version} → {version}")


def run(args: argparse.Namespace) -> None:
    log = init_logger()
    if args.command == "upgrade":
        _upgrade(args, log)
    elif args.command == "stage":
        native_assets.ensure(force=args.force)
    else:
        package = native_assets.REPO_ROOT / native_assets.PACKAGE_PATH
        for asset in native_assets.load().values():
            if args.command == "verify":
                native_assets.verify(asset, package)
            else:
                native_assets.clean(asset, package)


def main() -> None:
    args = parser().parse_args()
    try:
        run(args)
    except (ValueError, KeyError, OSError, zipfile.BadZipFile, requests.RequestException) as error:
        raise SystemExit(f"SDK {args.command} failed: {error}") from None
