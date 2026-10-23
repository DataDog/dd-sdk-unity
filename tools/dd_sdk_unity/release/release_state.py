# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Discover published versions from the existing tags and GitHub releases."""

import re

from .changelog import version_key
from .github_ops import GitHubOperations
from .models import VERSION_PATTERN, CompletedRelease


def completed_release(
    source: GitHubOperations, package: GitHubOperations, version: str
) -> CompletedRelease | None:
    release = package.release(version)
    if release is None or release.draft or release.prerelease:
        return None
    source_sha = source.reference_sha(f"tags/{version}")
    package_sha = package.reference_sha(f"tags/{version}")
    if source_sha is None or package_sha is None:
        return None
    return CompletedRelease(version=version, source_sha=source_sha, package_sha=package_sha)


def latest_completed(
    source: GitHubOperations, package: GitHubOperations, *, pending_version: str | None = None
) -> CompletedRelease:
    versions = [
        release.tag_name
        for release in package.repository.get_releases()
        if not release.draft
        and not release.prerelease
        and re.fullmatch(VERSION_PATTERN, release.tag_name)
        and release.tag_name != pending_version
    ]
    if not versions:
        raise ValueError("No completed release baseline exists")
    version = max(versions, key=version_key)
    completed = completed_release(source, package, version)
    if completed is None:
        raise ValueError(f"Latest release {version} is missing its source or package tag")
    return completed
