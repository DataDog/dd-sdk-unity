# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Prepare release metadata and read the reviewed release from Git and its PR."""

import json
import re

from github.PullRequest import PullRequest

from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.github import GitHubOperations
from dd_sdk_unity.scripts.common.log import get_default_logger
from dd_sdk_unity.scripts.common.versions.sdkversions import SdkVersionTable
from dd_sdk_unity.scripts.common.versions.semver import Version

from . import ASSEMBLY_PATH, CHANGELOG_PATH, NATIVE_HISTORY_PATH, PACKAGE_PATH
from .assemblyinfo import modify_assemblyinfo
from .changelog import (
    base_native_versions,
    collect_changes,
    merge_sections,
)
from .changelog_prompts import PRGroup
from .models import VERSION_PATTERN, Preparation
from .package import pins
from .package_version import modify_package_json
from .release_version import select_release


def prepare_plan(
    git: GitOperations,
    source: GitHubOperations,
    source_sha: str,
    *,
    base_version: str | None = None,
    release_version: str | None = None,
) -> Preparation:
    base = (
        base_version or json.loads(git.read(source_sha, f"{PACKAGE_PATH}/package.json"))["version"]
    )
    if base not in git.repository.tags:
        raise ValueError(f"Missing source tag {base}; set BASE_VERSION for a pre-bumped source")
    base_sha = git.repository.tags[base].commit.hexsha
    changes = collect_changes(git.repository, source, base_sha, source_sha)
    release = select_release(
        git.repository,
        source_sha,
        changes,
        base_version=base_version,
        release_version=release_version,
    )
    ios, android = pins(git, source_sha)
    return Preparation(
        release=release,
        ios=ios,
        android=android,
    )


def apply_metadata(git: GitOperations, plan: Preparation, notes: str) -> None:
    root = git.root
    version = Version.parse(plan.release.version)
    modify_package_json(str(root / PACKAGE_PATH / "package.json"), version)
    modify_assemblyinfo(str(root / ASSEMBLY_PATH), version)
    text = git.read(plan.release.source_sha, CHANGELOG_PATH).decode()
    (root / CHANGELOG_PATH).write_text(merge_sections(text, {plan.release.version: notes}))
    native_versions = SdkVersionTable.parse(
        git.read(plan.release.source_sha, NATIVE_HISTORY_PATH).decode()
    )
    native_versions.set(
        version,
        Version.parse(plan.ios.version),
        Version.parse(plan.android.version),
    )
    (root / NATIVE_HISTORY_PATH).write_text(native_versions.render())


def open_preparation(
    git: GitOperations,
    source: GitHubOperations,
    plan: Preparation,
    notes: str,
    *,
    token: str,
    groups: tuple[PRGroup, ...] = (),
) -> PullRequest:
    log = get_default_logger()
    version = plan.release.version
    title = f"chore: Prepare {version} release"
    candidate_ref = f"release/{version}"
    if source.reference_sha(f"heads/{candidate_ref}") is not None:
        raise ValueError(
            f"Candidate {candidate_ref} exists; inspect/cancel it before preparing again"
        )
    previous = base_native_versions(git, plan)
    apply_metadata(git, plan, notes)
    git.commit(title)
    review_ref = f"release-prep/{version}"
    source.create_reference(f"heads/{candidate_ref}", plan.release.source_sha)
    git.push(
        source,
        branch=review_ref,
        expected_parent=plan.release.source_sha,
        create_branch=True,
        token=token,
    )
    change_sections = [
        f"### {group.label}\n\n" + "\n".join(f"- #{pr.number}" for pr in group.prs)
        for group in groups
    ]
    noted = {pr.number for group in groups for pr in group.prs}
    others = [change.number for change in plan.release.changes if change.number not in noted]
    if others:
        change_sections.append(
            "### Other PRs not considered relevant for CHANGELOG\n\n"
            + "\n".join(f"- #{number}" for number in others)
        )
    changes = "\n\n".join(change_sections)
    body = f"Prepare **{version}** with changes since **{plan.release.base_version}**.\n"
    native_deltas = [
        f"- **{sdk}**: `{previous[sdk]}` → `{current}`"
        for sdk, current in (("iOS", plan.ios.version), ("Android", plan.android.version))
        if previous[sdk] != current
    ]
    if native_deltas:
        body += "\n## Native SDK deltas\n\n" + "\n".join(native_deltas) + "\n"
    if changes:
        body += f"\n## Changes\n\n{changes}\n"
    # Earlier cancelled attempts may have used the same branch names. This is a
    # new preparation PR; its GitHub number distinguishes it from those attempts.
    pull = source.repository.create_pull(
        base=candidate_ref, head=review_ref, title=title, body=body
    )
    log.info(f"Review preparation: {pull.html_url}")
    return pull


def reviewed_pull(
    source: GitHubOperations,
    ref: str,
    source_sha: str,
) -> None:
    """Require a merged preparation PR for this exact candidate branch and commit."""
    version = ref.removeprefix("release/")
    if not re.fullmatch(VERSION_PATTERN, version) or ref != f"release/{version}":
        raise ValueError("Expected a release/<SemVer> candidate branch")
    pulls = [
        pull
        for pull in source.repository.get_pulls(state="closed", base=ref)
        if pull.merged and pull.merge_commit_sha == source_sha
    ]
    if not pulls:
        raise ValueError("No merged preparation PR for this candidate SHA")
    if len(pulls) != 1:
        raise ValueError("Candidate must match exactly one merged preparation PR")
    pull = pulls[0]
    if (
        pull.base.ref != ref
        or pull.head.ref != f"release-prep/{version}"
        or pull.base.repo.full_name != source.repository.full_name
        or pull.head.repo.full_name != source.repository.full_name
        or source.reference_sha(f"heads/{ref}") != source_sha
    ):
        raise ValueError("Cancelled, replaced, or stale preparation candidate")
