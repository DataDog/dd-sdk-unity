# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Publish the assembled distribution after release review."""

import json
import tempfile
from pathlib import Path

from .changelog import version_key
from .git_ops import GitOperations
from .github_ops import GitHubOperations
from .models import CompletedRelease
from .package import (
    check_versions,
    extract_package,
    install,
)
from .preparation import check_metadata, preparation_base, reviewed_pull
from .release_state import completed_release, latest_completed


def completed_publication(
    source: GitHubOperations,
    package: GitHubOperations,
    version: str,
) -> CompletedRelease:
    record = completed_release(source, package, version)
    if record is None:
        raise ValueError("This release is incomplete; rerun its publication job")
    print(f"Release {version} is already published.")
    print(f"Source commit: {record.source_sha}; package commit: {record.package_sha}")
    print(f"Release: {package.repository.html_url}/releases/tag/{version}")
    return record


def _check_package_commit(
    git: GitOperations,
    github: GitHubOperations,
    sha: str,
    source_sha: str,
    tree_sha: str,
) -> None:
    commit = git.repository.commit(sha)
    if (
        not github.git_commit(sha).raw_data.get("verification", {}).get("verified")
        or f"Release-Source: {source_sha}\n" not in str(commit.message)
        or commit.tree.hexsha != tree_sha
    ):
        raise ValueError("Existing distribution commit differs from this release")


def publish(
    source: GitHubOperations,
    package: GitHubOperations,
    archive: Path,
    *,
    version: str,
    source_sha: str,
    package_token: str | None,
    dry_run: bool = False,
) -> CompletedRelease | None:
    version_key(version)
    completed = completed_release(source, package, version)
    if completed is not None:
        if completed.source_sha != source_sha:
            raise ValueError("Existing release belongs to a different source commit")
        print(f"Release {version} is already published.")
        print(f"Source commit: {completed.source_sha}; package commit: {completed.package_sha}")
        if dry_run:
            print("Skipping preparation branch deletion due to dry run.")
            return completed
        source.delete_branch(f"release-prep/{version}", source_sha)
        return completed
    pull = reviewed_pull(source, f"release-prep/{version}", source_sha)
    base_version, base_sha, cut_sha = preparation_base(pull)
    latest = latest_completed(source, package, pending_version=version)
    with tempfile.TemporaryDirectory(prefix="unity-publish-") as scratch:
        root = Path(scratch)
        sdk = GitOperations.clone(
            source.repository.clone_url,
            root / "source",
            revision=source_sha,
        )
        notes = check_metadata(sdk, cut_sha, source_sha, version)
        if (
            latest.version != base_version
            or latest.source_sha != base_sha
            or source.reference_sha(f"tags/{base_version}") != base_sha
            or version_key(version) <= version_key(base_version)
        ):
            raise ValueError("Reviewed release baseline or package metadata is no longer eligible")
        existing_source = source.reference_sha(f"tags/{version}")
        if existing_source not in (None, source_sha):
            raise ValueError("Source tag conflicts with the assembled candidate")
        extract_package(archive, root / "payload")
        check_versions(root / "payload", version)
        main_sha = package.reference_sha("heads/main")
        if main_sha is None:
            raise ValueError("Distribution repository has no main branch")
        distribution = GitOperations.clone(
            package.repository.clone_url,
            root / "distribution",
            revision=main_sha,
            branch="main",
        )
        install(root / "payload", distribution.root)
        # Force staging includes bundled binaries even when distribution ignores match them.
        distribution.repository.git.add("--all", "--force")
        tree_sha = distribution.repository.index.write_tree().hexsha
        package_sha = package.reference_sha(f"tags/{version}")
        push_needed = False
        if package_sha:
            _check_package_commit(distribution, package, package_sha, source_sha, tree_sha)
        elif distribution.repository.head.commit.tree.hexsha == tree_sha:
            _check_package_commit(distribution, package, main_sha, source_sha, tree_sha)
            package_sha = main_sha
        else:
            current_version = json.loads(distribution.read(main_sha, "package.json"))["version"]
            if version_key(current_version) >= version_key(version):
                raise ValueError("Distribution main already has this version or a newer payload")
            distribution.commit(
                f"chore(release): Publish {version}\n\nRelease-Source: {source_sha}\n"
            )
            push_needed = True
        release = package.release(version)
        if release and (release.draft or release.prerelease):
            raise ValueError("Existing GitHub release is not a completed public release")
        if release and not package_sha:
            raise ValueError("Existing GitHub release has no matching package tag")
        print(f"Preparing publication of Unity SDK {version}")
        print(f"Source commit: {source_sha}")
        print(f"Baseline: {base_version} at {base_sha}")
        print(f"Package archive: {archive}")
        print(f"Distribution main: {main_sha}")
        print(f"Release notes:\n{notes}")
        print("Distribution diff against main:")
        print(distribution.repository.git.diff(main_sha, "HEAD"))
        # Repeat observed-head checks immediately before writing. There is no cross-job lock.
        if (
            latest_completed(source, package, pending_version=version) != latest
            or package.reference_sha("heads/main") != main_sha
        ):
            raise ValueError("Publication state moved during preflight; inspect and rerun")
        reviewed_pull(source, f"release-prep/{version}", source_sha)
        if dry_run:
            print("Skipping tag creation, package push and GitHub publication due to dry run.")
            return None
        source.ensure_reference(f"tags/{version}", source_sha)
        if push_needed:
            package_sha = distribution.push(
                package, branch="main", expected_parent=main_sha, token=package_token
            )
        if not package_sha:
            raise RuntimeError("Missing verified signed package SHA")
        package.ensure_release(version, package_sha, notes)
        record = CompletedRelease(version=version, source_sha=source_sha, package_sha=package_sha)
        print(f"Published {version}: source {source_sha}; package {package_sha}")
        source.delete_branch(f"release-prep/{version}", source_sha)
        return record
