# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Publish the assembled distribution after release review."""

import json
import tempfile
from pathlib import Path

from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.github import GitHubOperations
from dd_sdk_unity.scripts.common.log import get_default_logger

from .changelog import sections, version_key
from .package import (
    check_versions,
    extract_package,
    install,
)
from .preparation import reviewed_pull


def publish(
    source: GitHubOperations,
    package: GitHubOperations,
    archive: Path,
    *,
    version: str,
    source_sha: str,
    package_token: str | None,
    dry_run: bool = False,
) -> None:
    log = get_default_logger()
    version_key(version)
    if (
        source.reference_sha(f"tags/{version}")
        or package.reference_sha(f"tags/{version}")
        or package.release(version)
    ):
        raise ValueError(f"Release {version} already exists; inspect and recover manually")
    reviewed_pull(source, f"release/{version}", source_sha)
    with tempfile.TemporaryDirectory(prefix="unity-publish-") as scratch:
        root = Path(scratch)
        extract_package(archive, root / "payload")
        check_versions(root / "payload", version)
        notes = sections((root / "payload/CHANGELOG.md").read_text())[1][version]
        main_sha = package.reference_sha("heads/main")
        if main_sha is None:
            raise ValueError("Distribution repository has no main branch")
        distribution = GitOperations.clone(
            package.repository.clone_url,
            root / "distribution",
            revision=main_sha,
            branch="main",
        )
        current_version = json.loads(distribution.read(main_sha, "package.json"))["version"]
        if version_key(current_version) >= version_key(version):
            raise ValueError("Distribution main already has this version or a newer payload")
        install(root / "payload", distribution.root)
        # Include bundled binaries even when distribution ignore rules match them.
        distribution.repository.git.add("--all", "--force")
        distribution.commit(f"chore(release): Publish {version}")
        log.info(f"Preparing publication of Unity SDK {version}")
        log.info(f"Source commit: {source_sha}")
        log.info(f"Package archive: {archive}")
        log.info(f"Distribution main: {main_sha}")
        log.info(f"Release notes:\n{notes}")
        log.info("Distribution diff against main:")
        log.info(distribution.repository.git.diff(main_sha, "HEAD"))
        if dry_run:
            log.info("Skipping tag creation, package push and GitHub publication due to dry run.")
            return
        source.create_reference(f"tags/{version}", source_sha)
        package_sha = distribution.push(
            package, branch="main", expected_parent=main_sha, token=package_token
        )
        package.create_release(version, package_sha, notes)
        log.info(f"Published {version}: source {source_sha}; package {package_sha}")
        source.delete_branch(f"release/{version}", source_sha)
