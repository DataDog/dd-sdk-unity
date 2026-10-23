# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Version-file baseline selection and deterministic patch/minor inference."""

import json

from git import Repo

from . import ASSEMBLY_PATH, CHANGELOG_PATH, PACKAGE_PATH
from .changelog import HEADINGS, version_key
from .git_ops import GitOperations
from .models import Change, CompletedRelease, ReleasePlan

# Audited migration exception: this tag has only a release metadata tail; its
# preparation parent is integrated into develop. Never generalize by path alone.
LEGACY_BASE_SHA = "5f149e623695a1ab776618299f9165952fa7b877"
LEGACY_METADATA = {
    f"{PACKAGE_PATH}/package.json",
    ASSEMBLY_PATH,
    f"{PACKAGE_PATH}/Editor/DatadogDependencies.xml",
}


def merge_package_version(development_json: str, incoming_version: str) -> str:
    """Advance only the version field; preserve a higher development version/fields."""
    package = json.loads(development_json)
    if not isinstance(package, dict) or not isinstance(package.get("version"), str):
        raise TypeError("Development package.json must contain a string version")
    if version_key(incoming_version) <= version_key(package["version"]):
        return development_json
    package["version"] = incoming_version
    return json.dumps(package, indent=2) + "\n"


def check_baseline_ancestry(repo: Repo, base_sha: str, source_sha: str) -> None:
    """Require the released SDK code in the cut, including the audited legacy tail."""
    tagged, source = repo.commit(base_sha), repo.commit(source_sha)
    if repo.is_ancestor(tagged, source):
        return
    parent = tagged.parents[0] if len(tagged.parents) == 1 else None
    changed = {diff.a_path or diff.b_path for diff in parent.diff(tagged)} if parent else set()
    if not (
        tagged.hexsha == LEGACY_BASE_SHA
        and parent is not None
        and repo.is_ancestor(parent, source)
        and changed
        and changed <= LEGACY_METADATA
    ):
        raise ValueError("Released code from the selected base is not integrated into the source")


def select_release(
    repo: Repo,
    source_sha: str,
    completed: CompletedRelease,
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
    if base != completed.version:
        raise ValueError(
            f"Base {base} is not the latest completed release ({completed.version}); "
            "specify BASE_VERSION when the package is pre-bumped"
        )
    tags = {tag.name: tag.commit for tag in repo.tags}
    if base not in tags or tags[base].hexsha != completed.source_sha:
        raise ValueError(f"Source tag {base} does not match its completed release")
    tagged = tags[base]
    check_baseline_ancestry(repo, tagged.hexsha, source.hexsha)
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
        raise ValueError(
            f"Target {selected} already has a release or source tag; inspect/resume it"
        )
    return ReleasePlan(
        source_sha=source.hexsha,
        declared_version=declared,
        base_version=base,
        base_sha=tagged.hexsha,
        base_override=base_version,
        version_override=release_version,
        inferred_version=inferred,
        version=selected,
        changes=changes,
    )
