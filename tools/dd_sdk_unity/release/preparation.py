# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Prepare release metadata and read the reviewed release from Git and its PR."""

import json
import re
from pathlib import Path

from github.PullRequest import PullRequest

from dd_sdk_unity.scripts.common.log import get_default_logger

from . import ASSEMBLY_PATH, CHANGELOG_PATH, NATIVE_HISTORY_PATH, PACKAGE_PATH
from .changelog import (
    base_native_versions,
    collect_changes,
    merge_native_history,
    merge_sections,
)
from .changelog_prompts import PRGroup
from .git_ops import GitOperations
from .github_ops import GitHubOperations
from .models import VERSION_PATTERN, Preparation
from .package import pins
from .release_version import select_release


def set_version(root: Path, version: str) -> None:
    package_path = root / PACKAGE_PATH / "package.json"
    package = json.loads(package_path.read_text())
    package["version"] = version
    package_path.write_text(json.dumps(package, indent=2) + "\n")
    path = root / ASSEMBLY_PATH
    assembly, count = re.subn(
        r'AssemblyVersion\("[^"\n]+"\)', f'AssemblyVersion("{version}")', path.read_text()
    )
    if count != 1:
        raise ValueError("Expected exactly one AssemblyVersion")
    path.write_text(assembly)


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
    set_version(root, plan.release.version)
    text = git.read(plan.release.source_sha, CHANGELOG_PATH).decode()
    (root / CHANGELOG_PATH).write_text(merge_sections(text, {plan.release.version: notes}))
    (root / NATIVE_HISTORY_PATH).write_text(
        merge_native_history(
            git.read(plan.release.source_sha, NATIVE_HISTORY_PATH).decode(),
            {plan.release.version: (plan.ios.version, plan.android.version)},
        )
    )


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
    preparation_sha = git.commit(title)
    review_ref = f"release-notes/{version}-{preparation_sha[:12]}"
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
    body = (
        f"Prepare **{version}** with changes since **{plan.release.base_version}**.\n\n"
        "## Native SDK deltas\n\n"
        f"- **iOS**: `{previous['iOS']}` → `{plan.ios.version}`\n"
        f"- **Android**: `{previous['Android']}` → `{plan.android.version}`\n"
    )
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
        raise ValueError("Expected a release/X.Y.Z candidate branch")
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
        or not pull.head.ref.startswith(f"release-notes/{version}-")
        or pull.base.repo.full_name != source.repository.full_name
        or pull.head.repo.full_name != source.repository.full_name
        or source.reference_sha(f"heads/{ref}") != source_sha
    ):
        raise ValueError("Cancelled, replaced, or stale preparation candidate")
