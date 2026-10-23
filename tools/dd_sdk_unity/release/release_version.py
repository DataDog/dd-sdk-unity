# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Version-file baseline selection and deterministic patch/minor inference."""

import json

from git import Repo

from . import CHANGELOG_PATH, PACKAGE_PATH
from .changelog import HEADINGS, version_key
from .git_ops import GitOperations
from .models import Change, ReleasePlan


def merge_package_version(development_json: str, incoming_version: str) -> str:
    """Advance only the version field; preserve a higher development version/fields."""
    package = json.loads(development_json)
    if not isinstance(package, dict) or not isinstance(package.get("version"), str):
        raise TypeError("Development package.json must contain a string version")
    if version_key(incoming_version) <= version_key(package["version"]):
        return development_json
    package["version"] = incoming_version
    return json.dumps(package, indent=2) + "\n"


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
    included = tuple(change for change in changes if not change.bookkeeping)
    if not included:
        raise ValueError("There are no releasable changes")
    major, minor, patch = version_key(base)
    inferred = (
        f"{major}.{minor + 1}.0"
        if any(change.kind == "feat" for change in included)
        else f"{major}.{minor}.{patch + 1}"
    )
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
