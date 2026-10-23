# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Prepare release metadata and read the reviewed release from Git and its PR."""

import json
import re
from pathlib import Path

from github.PullRequest import PullRequest

from . import ASSEMBLY_PATH, CHANGELOG_PATH, NATIVE_HISTORY_PATH, PACKAGE_PATH
from .changelog import (
    collect_changes,
    merge_native_history,
    merge_sections,
    sections,
    split_legacy_notes,
)
from .git_ops import GitOperations
from .github_ops import GitHubOperations
from .models import VERSION_PATTERN, Preparation
from .package import pins
from .release_state import latest_completed
from .release_version import select_release

BASE = re.compile(
    rf"<!-- unity-release-base version=({VERSION_PATTERN}) "
    r"sha=([0-9a-f]{40}) source=([0-9a-f]{40}) -->"
)


class CandidateNotApproved(ValueError):
    """Branch creation precedes its preparation PR; that pipeline has nothing to assemble."""


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
    package: GitHubOperations,
    source_sha: str,
    *,
    base_version: str | None = None,
    release_version: str | None = None,
) -> Preparation:
    completed = latest_completed(source, package)
    base = (
        base_version or json.loads(git.read(source_sha, f"{PACKAGE_PATH}/package.json"))["version"]
    )
    base_sha = source.reference_sha(f"tags/{base}")
    if base_sha is None:
        raise ValueError(f"Missing source tag {base}; set BASE_VERSION for a pre-bumped source")
    changes = collect_changes(git.repository, source, base_sha, source_sha)
    release = select_release(
        git.repository,
        source_sha,
        completed,
        changes,
        base_version=base_version,
        release_version=release_version,
    )
    ios, android = pins(git, source_sha)
    _, legacy = split_legacy_notes(git.read(source_sha, CHANGELOG_PATH).decode())
    return Preparation(
        release=release,
        ios=ios,
        android=android,
        legacy_notes=legacy,
    )


def apply_metadata(git: GitOperations, plan: Preparation, notes: str) -> None:
    root = git.root
    set_version(root, plan.release.version)
    text, _ = split_legacy_notes(git.read(plan.release.source_sha, CHANGELOG_PATH).decode())
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
    package: GitHubOperations,
    plan: Preparation,
    notes: str,
    *,
    token: str,
) -> PullRequest:
    version = plan.release.version
    candidate_ref = f"release-prep/{version}"
    if source.reference_sha(f"heads/{candidate_ref}") is not None:
        raise ValueError(
            f"Candidate {candidate_ref} exists; inspect/cancel it before preparing again"
        )
    if (
        source.reference_sha(f"tags/{version}")
        or package.reference_sha(f"tags/{version}")
        or package.release(version)
    ):
        raise ValueError(f"Publication {version} already exists; inspect it before preparing")
    apply_metadata(git, plan, notes)
    preparation_sha = git.commit(f"chore(release): Prepare {version}")
    review_ref = f"release-notes/{version}-{preparation_sha[:12]}"
    source.ensure_reference(f"heads/{candidate_ref}", plan.release.source_sha)
    git.push(
        source,
        branch=review_ref,
        expected_parent=plan.release.source_sha,
        create_branch=True,
        token=token,
    )
    compare = (
        f"{source.repository.html_url}/compare/{plan.release.base_sha}...{plan.release.source_sha}"
    )
    inventory = "\n".join(
        f"- {change.identifier}: {change.title}" for change in plan.release.changes
    )
    body = (
        f"Prepare **{version}** from `{plan.release.source_sha}`.\n\n"
        f"Declared package: `{plan.release.declared_version}`; base: "
        f"`{plan.release.base_version}` (`{plan.release.base_sha}`).\n"
        f"BASE_VERSION: `{plan.release.base_override or ''}`; RELEASE_VERSION: "
        f"`{plan.release.version_override or ''}`; inferred target: "
        f"`{plan.release.inferred_version}`.\n\n"
        f"Review the [captured SDK changes]({compare}) as well as this metadata diff. "
        "Edit notes here; additional SDK code requires cancelling and preparing again.\n\n"
        f"{inventory}\n\n"
        f"<!-- unity-release-base version={plan.release.base_version} "
        f"sha={plan.release.base_sha} source={plan.release.source_sha} -->"
    )
    # Earlier cancelled attempts may have used the same branch names. This is a
    # new preparation PR; its GitHub number distinguishes it from those attempts.
    pull = source.repository.create_pull(
        base=candidate_ref, head=review_ref, title=f"chore(release): Prepare {version}", body=body
    )
    print(f"Review preparation: {pull.html_url}")
    return pull


def reviewed_pull(
    source: GitHubOperations,
    ref: str,
    source_sha: str,
) -> PullRequest:
    """Find the merged review for this exact candidate branch and commit."""
    version = ref.removeprefix("release-prep/")
    if not re.fullmatch(VERSION_PATTERN, version) or ref != f"release-prep/{version}":
        raise ValueError("Expected a release-prep/X.Y.Z candidate branch")
    pulls = [
        pull
        for pull in source.repository.get_pulls(state="closed", base=ref)
        if pull.merged and pull.merge_commit_sha == source_sha
    ]
    if not pulls:
        raise CandidateNotApproved("No merged preparation PR for this candidate SHA")
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
    if not source.git_commit(source_sha).raw_data.get("verification", {}).get("verified"):
        raise ValueError("Approved source commit lacks a verified signature")
    return pull


def preparation_base(pull: PullRequest) -> tuple[str, str, str]:
    """Return the selected baseline version/SHA and original code cut from the PR."""
    matches = BASE.findall(pull.body or "")
    if len(matches) != 1:
        raise ValueError("Preparation PR must record one selected baseline and source cut")
    return matches[0]


def check_metadata(git: GitOperations, cut_sha: str, final_sha: str, version: str) -> str:
    cut, final = git.repository.commit(cut_sha), git.repository.commit(final_sha)
    if not git.repository.is_ancestor(cut, final):
        raise ValueError("Approved source does not descend from its captured source")
    allowed = {f"{PACKAGE_PATH}/package.json", ASSEMBLY_PATH, CHANGELOG_PATH, NATIVE_HISTORY_PATH}
    if any(
        (diff.a_path not in allowed or diff.b_path not in allowed or diff.a_mode != diff.b_mode)
        for diff in cut.diff(final)
    ):
        raise ValueError("Preparation contains SDK, dependency, or unexpected file changes")
    before = json.loads(git.read(cut.hexsha, f"{PACKAGE_PATH}/package.json"))
    after = json.loads(git.read(final_sha, f"{PACKAGE_PATH}/package.json"))
    if after.pop("version") != version:
        raise ValueError("Approved package version differs from the recorded target")
    before.pop("version")
    if before != after:
        raise ValueError("Preparation changed package fields other than version")
    assembly = git.read(final_sha, ASSEMBLY_PATH).decode()
    if assembly.count(f'AssemblyVersion("{version}")') != 1:
        raise ValueError("Approved assembly version differs from the recorded target")
    normalize = lambda text: re.sub(r'AssemblyVersion\("[^"\n]+"\)', 'AssemblyVersion("")', text)
    if normalize(assembly) != normalize(git.read(cut.hexsha, ASSEMBLY_PATH).decode()):
        raise ValueError("Preparation changed assembly metadata other than version")
    original, _ = split_legacy_notes(git.read(cut.hexsha, CHANGELOG_PATH).decode())
    prefix, old_sections = sections(original)
    new_prefix, new_sections = sections(git.read(final_sha, CHANGELOG_PATH).decode())
    notes = new_sections.pop(version, None)
    if not notes or prefix != new_prefix or old_sections != new_sections:
        raise ValueError("Preparation changed published history or has no approved notes")
    ios, android = pins(git, cut_sha)
    expected_history = merge_native_history(
        git.read(cut.hexsha, NATIVE_HISTORY_PATH).decode(),
        {version: (ios.version, android.version)},
    )
    observed_history = merge_native_history(git.read(final_sha, NATIVE_HISTORY_PATH).decode(), {})
    if expected_history != observed_history:
        raise ValueError("Preparation changed native history or dependency pins")
    return notes
