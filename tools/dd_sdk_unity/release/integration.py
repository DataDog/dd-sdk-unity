# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Open an ancestry-preserving integration PR; preview without remote mutation."""

import json
import tempfile
from pathlib import Path
from textwrap import dedent

from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.github import GitHubOperations
from dd_sdk_unity.scripts.common.log import get_default_logger
from dd_sdk_unity.scripts.common.versions.sdkversions import SdkVersionTable
from dd_sdk_unity.scripts.common.versions.semver import Version

from . import ASSEMBLY_PATH, CHANGELOG_PATH, NATIVE_HISTORY_PATH, PACKAGE_PATH
from .assemblyinfo import modify_assemblyinfo
from .changelog import merge_sections, sections
from .package import pins
from .package_version import merge_package_version


def integrate(
    source: GitHubOperations,
    *,
    version: str,
    source_sha: str,
    source_token: str | None,
    dry_run: bool = False,
) -> None:
    log = get_default_logger()
    branch = f"merge-back/{version}"
    existing = list(
        source.repository.get_pulls(
            state="all", base="develop", head=f"{source.repository.owner.login}:{branch}"
        )
    )
    if len(existing) > 1:
        raise ValueError(f"Multiple integration PRs exist for {version}")
    if existing:
        pull = existing[0]
        if pull.state == "closed" and not pull.merged:
            raise ValueError(f"Integration PR #{pull.number} was closed; inspect it")
        if pull.merged:
            log.info(f"Release {version} is already integrated: {pull.html_url}")
            return
        log.info(f"Reuse integration PR: {pull.html_url}")
        return
    develop_sha = source.reference_sha("heads/develop")
    if develop_sha is None:
        raise ValueError("Source repository has no develop branch")
    remote = source.reference_sha(f"heads/{branch}")
    with tempfile.TemporaryDirectory(prefix="unity-integrate-") as scratch:
        git = GitOperations.clone(
            source.repository.clone_url,
            Path(scratch) / "source",
            revision=source_sha,
            branch=branch,
        )
        notes = sections(git.read(source_sha, CHANGELOG_PATH).decode())[1][version]
        root = git.root
        development_package = git.read(develop_sha, f"{PACKAGE_PATH}/package.json").decode()
        merged_package = merge_package_version(development_package, version)
        merged_version = json.loads(merged_package)["version"]
        (root / PACKAGE_PATH / "package.json").write_text(merged_package)
        assembly_path = root / ASSEMBLY_PATH
        assembly_path.write_bytes(git.read(develop_sha, ASSEMBLY_PATH))
        modify_assemblyinfo(str(assembly_path), Version.parse(merged_version))
        development_notes = git.read(develop_sha, CHANGELOG_PATH).decode()
        (root / CHANGELOG_PATH).write_text(merge_sections(development_notes, {version: notes}))
        ios, android = pins(git, source_sha)
        native_versions = SdkVersionTable.parse(git.read(develop_sha, NATIVE_HISTORY_PATH).decode())
        native_versions.set(
            Version.parse(version), Version.parse(ios.version), Version.parse(android.version)
        )
        native_versions.rows.sort(key=lambda row: row.unity_version, reverse=True)
        (root / NATIVE_HISTORY_PATH).write_text(native_versions.render())
        if git.repository.is_dirty(untracked_files=True):
            git.commit(f"chore(release): Integrate {version}")
        title = f"chore(release): Integrate {version} into develop"
        body = dedent(f"""\
            Integrate published Unity SDK **{version}** from `{source_sha}`.

            Prepared against develop `{develop_sha}`. Keep higher development versions and newer work when resolving conflicts. Merge with a **merge commit** to preserve release ancestry; do not squash or rebase this PR.

            Release: https://github.com/DataDog/unity-package/releases/tag/{version}
            """)
        if remote:
            if not git.repository.is_ancestor(source_sha, remote):
                raise ValueError("Existing integration branch has different release ancestry")
            # Tree hashes compare files/modes, independent of commit timestamps.
            if git.repository.commit(remote).tree.hexsha != git.repository.head.commit.tree.hexsha:
                raise ValueError("Existing integration branch differs; inspect before retrying")
        log.info(f"Preparing integration PR: {branch} -> develop")
        log.info(f"Source commit: {source_sha}; develop commit: {develop_sha}")
        log.info(f"Package version after integration: {merged_version}")
        log.info(f"PR title: {title}\nPR body:\n{body}")
        log.info("Proposed PR diff:")
        log.info(git.repository.git.diff(f"{develop_sha}...HEAD"))
        if dry_run:
            log.info("Skipping branch push and PR creation due to dry run.")
            return
        if remote is None:
            if git.repository.head.commit.hexsha == source_sha:
                source.create_reference(f"heads/{branch}", source_sha)
            else:
                git.push(
                    source,
                    branch=branch,
                    expected_parent=source_sha,
                    create_branch=True,
                    token=source_token,
                )
        pull = source.repository.create_pull(base="develop", head=branch, title=title, body=body)
        log.info(f"Integration PR: {pull.html_url}; merge with a merge commit")
