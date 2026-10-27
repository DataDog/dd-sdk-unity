# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Version-file baseline selection and deterministic patch/minor inference."""

import json

from git import Repo

from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.versions.semver import Version, VersionBump

from . import CHANGELOG_PATH, PACKAGE_PATH
from .changelog import HEADINGS, version_key
from .models import Change, ReleasePlan


def select_release(
    repo: Repo,
    source_sha: str,
    changes: tuple[Change, ...],
    *,
    base_version: str | None = None,
    release_version: str | None = None,
) -> ReleasePlan:
    git = GitOperations(repo)
    source = repo.commit(source_sha)
    declared = str(json.loads(git.read(source.hexsha, f"{PACKAGE_PATH}/package.json"))["version"])
    version_key(declared)
    base = base_version if base_version is not None else declared
    version_key(base)
    tags = {tag.name: tag.commit for tag in repo.tags}
    if base not in tags:
        raise ValueError(f"Missing source tag {base}; specify BASE_VERSION for a pre-bumped source")
    tagged = tags[base]
    headings = list(HEADINGS.finditer(git.read(source.hexsha, CHANGELOG_PATH).decode()))
    if not headings or headings[0].group(1) != base:
        raise ValueError("The latest versioned changelog heading does not match the selected base")
    if not changes:
        raise ValueError("There are no releasable changes")
    bump = VersionBump.MINOR if any(change.kind == "feat" for change in changes) else VersionBump.PATCH
    inferred = str(Version.parse(base).bump(bump))
    selected = release_version if release_version is not None else inferred
    if version_key(selected) <= version_key(base):
        raise ValueError("The target version must be greater than the base")
    if selected in tags:
        raise ValueError(f"Target {selected} already has a source tag")
    return ReleasePlan(
        source_sha=source.hexsha,
        base_version=base,
        base_sha=tagged.hexsha,
        version=selected,
        changes=changes,
    )
