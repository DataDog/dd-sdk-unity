# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Release handoffs with mocked GitPython and real package files and archives."""

import json
import plistlib
import zipfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, Mock

import pytest
from git import Repo

from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.github import GitHubOperations

from . import (
    ANDROID_PIN_PATH,
    ASSEMBLY_PATH,
    CHANGELOG_PATH,
    IOS_PIN_PATH,
    NATIVE_HISTORY_PATH,
    PACKAGE_PATH,
)
from .changelog import draft_notes
from .changelog_prompts import (
    ChangelogEntry,
    ChangelogEntryList,
    GroupedPR,
    GroupedPRs,
    PRGroup,
)
from .integration import integrate
from .package import (
    archive_package,
    assemble,
    assemble_package,
    check_versions,
    extract_package,
    sha256,
)
from .preparation import open_preparation, prepare_plan, reviewed_pull
from .publish import publish

BASE = "a" * 40
CUT = "b" * 40
REVIEWED = "c" * 40
DEVELOP = "d" * 40
MAIN = "e" * 40
LOCAL = "f" * 40
SIGNED = "1" * 40


def write_files(root, files):
    for name, data in files.items():
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data.encode() if isinstance(data, str) else data)


@pytest.fixture
def workflow(tmp_path, monkeypatch, repository, make_commit):
    archive = tmp_path / "native.zip"
    libraries = [
        {"SupportedPlatform": "ios"},
        {"SupportedPlatform": "ios", "SupportedPlatformVariant": "simulator"},
        {"SupportedPlatform": "tvos"},
    ]
    with zipfile.ZipFile(archive, "w") as zipped:
        prefix = "Datadog.xcframework/DatadogCore.xcframework/"
        zipped.writestr(prefix + "Info.plist", plistlib.dumps({"AvailableLibraries": libraries}))
        for name in ("ios-arm64/DatadogCore.framework/DatadogCore", "tvos-arm64/symbols.dSYM/bin"):
            zipped.writestr(prefix + name, b"native binary fixture")
        zipped.writestr("Datadog.xcframework/Unselected.xcframework/NotBundled", b"unused")
    files = {
        f"{PACKAGE_PATH}/package.json": json.dumps({"version": "1.7.1", "name": "fixture"}),
        ASSEMBLY_PATH: (
            '[assembly: AssemblyVersion("1.7.1")]\n'
            '[assembly: AssemblyInformationalVersion("1.7.1")]\n'
        ),
        CHANGELOG_PATH: "# Change Log\n\n## 1.7.1\n\n- Original notes.\n",
        NATIVE_HISTORY_PATH: "| Unity SDK | iOS SDK | Android SDK |\n|---|---|---|\n| 1.7.1 | 1.0.0 | 1.0.0 |\n",
        IOS_PIN_PATH: json.dumps(
            {"version": "1.0.0", "sha256": sha256(archive), "modules": ["DatadogCore"]}
        ),
        ANDROID_PIN_PATH: '{"version":"1.0.0","artifacts":["rum"]}',
        f"{PACKAGE_PATH}/Runtime/Code.cs": "fixed\n",
        f"{PACKAGE_PATH}/NOTICE.txt": "Committed notice\n",
        f"{PACKAGE_PATH}/Tests/DoNotShip.cs": "test\n",
        f"{PACKAGE_PATH}/Tests.meta": "test metadata\n",
        f"{PACKAGE_PATH}/README.md": "# Package\n[//]: # (Repo Note)\n",
        f"{PACKAGE_PATH}/Editor/com.datadoghq.unity.editor.asmdef": "{}\n",
        f"{PACKAGE_PATH}/Editor/com.datadoghq.unity.Editor.asmdef.meta": "guid: preserved-editor-guid\n",
        f"{PACKAGE_PATH}/Plugins/iOS/DatadogCore.xcframework.meta": "guid: preserved-plugin-guid\n",
        "tools/snippets/deployment_repo.md": "This is the distribution.\n",
        "LICENSE": "License bytes\n",
    }
    checkout = tmp_path / "source"
    write_files(checkout, files)
    commits = {sha: make_commit(sha, files) for sha in (BASE, CUT, DEVELOP)}
    repository.working_tree_dir = str(checkout)
    repository.commit.side_effect = commits.__getitem__
    repository.head.commit = commits[CUT]
    repository.heads = {}
    repository.iter_commits.return_value = [commits[CUT]]
    tag = SimpleNamespace(name="1.7.1", commit=commits[BASE])
    repository.tags.__iter__.side_effect = lambda: iter([tag])
    repository.tags.__contains__.side_effect = lambda name: name == tag.name
    repository.tags.__getitem__.side_effect = {tag.name: tag}.__getitem__

    repository.index.commit.return_value = make_commit(LOCAL)
    source = MagicMock(spec=GitHubOperations)
    source.__enter__.return_value = source
    source.repository = Mock()
    source.repository.full_name = "fixture/source"
    source.repository.clone_url = "https://github.invalid/fixture/source"
    source.repository.owner.login = "fixture"
    source.token = "fake-source"
    refs = {"heads/develop": DEVELOP}
    source.reference_sha.side_effect = refs.get
    source.create_reference.side_effect = refs.__setitem__
    source.merged_pull_numbers_since.return_value = [1]
    source.repository.get_pull.return_value = SimpleNamespace(
        number=1, title="fix: Prevent crash", body="Corrects an SDK crash.", merge_commit_sha=CUT
    )
    source.repository.get_pulls.return_value = []
    source.repository.create_pull.side_effect = lambda **fields: SimpleNamespace(
        number=2, html_url="https://github.invalid/fixture/source/pull/2", **fields
    )
    package = Mock(spec=GitHubOperations)
    package.repository = Mock()
    package.repository.full_name = "fixture/package"
    package.repository.clone_url = "https://github.invalid/fixture/package"
    package.reference_sha.side_effect = lambda ref: MAIN if ref == "heads/main" else None
    package.release.return_value = None
    distribution = MagicMock(spec=Repo)
    distribution.working_tree_dir = str(tmp_path / "distribution")
    distribution.commit.return_value = make_commit(MAIN, {"package.json": '{"version":"1.7.1"}'})
    distribution.heads = {}
    distribution.index.commit.return_value = make_commit(LOCAL)
    write_files(Path(distribution.working_tree_dir), {
        ".github/CODEOWNERS": "owner\n",
        ".gitignore": "*.xcframework/\n*.cs\n",
        "package.json": '{"version":"1.7.1"}',
        "obsolete.txt": "old payload\n",
    })
    repositories = {
        source.repository.clone_url: repository,
        package.repository.clone_url: distribution,
    }
    clone = Mock(side_effect=lambda url, *args, **kwargs: repositories[url])
    monkeypatch.setattr(Repo, "clone_from", clone)
    push = Mock(return_value=SIGNED)
    monkeypatch.setattr(GitOperations, "push", push)
    return SimpleNamespace(
        root=tmp_path, git=GitOperations(repository), repo=repository, source=source,
        package=package, distribution=distribution, archive=archive, files=files,
        commits=commits, make_commit=make_commit, refs=refs, clone=clone, push=push,
    )


def prepare_package(w, **versions):
    w.plan = prepare_plan(w.git, w.source, CUT, **versions)
    w.pull = open_preparation(w.git, w.source, w.plan, "- Fixed a crash.", token="fake-source")
    # Supply the reviewed commit returned by GitHub after the preparation PR merges.
    reviewed_files = {name: (w.git.root / name).read_bytes() for name in w.files}
    w.commits[REVIEWED] = w.make_commit(REVIEWED, reviewed_files)
    w.repo.head.commit = w.commits[REVIEWED]
    version = w.plan.release.version
    w.refs[f"heads/release/{version}"] = REVIEWED
    approved = SimpleNamespace(
        merged=True, merge_commit_sha=REVIEWED,
        base=SimpleNamespace(ref=f"release/{version}", repo=w.source.repository),
        head=SimpleNamespace(ref=f"release-prep/{version}", repo=w.source.repository),
    )
    w.source.repository.get_pulls.return_value = [approved]
    w.package_archive = w.root / "package.tar.gz"
    assemble_package(w.git, REVIEWED, w.archive, w.package_archive)
    return w


def publish_fixture(w, *, dry_run=False):
    publish(
        w.source, w.package, w.package_archive, version=w.plan.release.version,
        source_sha=REVIEWED, package_token="fake-package", dry_run=dry_run,
    )


def test_prerelease_preparation_publication_and_integration(workflow):
    version = "2.0.0-preview.1+build.5"
    w = prepare_package(workflow, base_version="1.7.1", release_version=version)
    assert w.plan.release.version == version
    assert w.pull.base == f"release/{version}"
    publish_fixture(w)
    w.package.create_release.assert_called_once_with(version, SIGNED, "- Fixed a crash.")
    payload = Path(w.distribution.working_tree_dir)
    assert json.loads((payload / "package.json").read_text())["version"] == version
    metadata = (payload / "Runtime/AssemblyInfo.cs").read_text()
    assert 'AssemblyVersion("2.0.0")' in metadata
    assert f'AssemblyInformationalVersion("{version}")' in metadata

    w.source.repository.get_pulls.return_value = []
    w.repo.is_dirty.return_value = True
    w.repo.head.commit = w.make_commit(LOCAL)
    integrate(w.source, source_sha=REVIEWED, version=version, source_token="fake-source")
    assert json.loads((w.git.root / PACKAGE_PATH / "package.json").read_text())["version"] == version
    assert f"## {version}" in (w.git.root / CHANGELOG_PATH).read_text()
    assert version in (w.git.root / NATIVE_HISTORY_PATH).read_text()
    w.repo.create_head.assert_any_call("release-base", REVIEWED)
    w.push.assert_called_with(
        w.source, branch=f"merge-back/{version}", expected_parent=REVIEWED,
        create_branch=True, token="fake-source",
    )


@pytest.mark.parametrize("attribute", ["AssemblyVersion", "AssemblyInformationalVersion"])
def test_publication_rejects_stale_prerelease_assembly_metadata(tmp_path, attribute):
    (tmp_path / "Runtime").mkdir()
    (tmp_path / "package.json").write_text('{"version":"2.0.0-preview1"}')
    metadata = (
        '[assembly: AssemblyVersion("2.0.0")]\n'
        '[assembly: AssemblyInformationalVersion("2.0.0-preview1")]\n'
    )
    assembly = tmp_path / "Runtime/AssemblyInfo.cs"
    assembly.write_text(metadata)
    check_versions(tmp_path, "2.0.0-preview1")
    assembly.write_text(metadata.replace(f'{attribute}("2.0.0', f'{attribute}("1.0.0'))
    with pytest.raises(ValueError, match="version differs"):
        check_versions(tmp_path, "2.0.0-preview1")


@pytest.mark.parametrize(
    ("ios_version", "android_version"),
    [("1.0.0", "1.0.0"), ("1.1.0", "1.0.0"), ("1.0.0", "1.1.0"), ("1.1.0", "1.1.0")],
)
def test_preparation_pr_reuses_generated_groups(workflow, ios_version, android_version):
    w = workflow
    plan = prepare_plan(w.git, w.source, CUT)
    change = plan.release.changes[0]
    plan = plan.model_copy(update={
        "release": plan.release.model_copy(update={"changes": (
            change.model_copy(update={"number": 8}),
            change.model_copy(update={"number": 9, "title": "ci: Update runners"}),
        )}),
        "ios": plan.ios.model_copy(update={"version": ios_version}),
        "android": plan.android.model_copy(update={"version": android_version}),
    })
    pull = open_preparation(
        w.git, w.source, plan, "- Fixed a crash.", token="fake-token",
        groups=(PRGroup(label="Crash fixes", prs=[GroupedPR(number=8, title=change.title)]),),
    )
    assert "### Crash fixes\n\n- #8" in pull.body
    assert "### Other PRs not considered relevant for CHANGELOG\n\n- #9" in pull.body
    assert "- Fixed a crash." not in pull.body
    assert ("## Native SDK deltas" in pull.body) == (
        ios_version != "1.0.0" or android_version != "1.0.0"
    )
    assert (f"- **iOS**: `1.0.0` → `{ios_version}`" in pull.body) == (ios_version != "1.0.0")
    assert (f"- **Android**: `1.0.0` → `{android_version}`" in pull.body) == (
        android_version != "1.0.0"
    )
    assert pull.title == "chore: Prepare 1.7.2 release"
    assert pull.head == "release-prep/1.7.2"
    assert pull.base == "release/1.7.2"
    w.source.create_reference.assert_called_once_with("heads/release/1.7.2", CUT)
    w.push.assert_called_once_with(
        w.source, branch="release-prep/1.7.2", expected_parent=CUT,
        create_branch=True, token="fake-token",
    )
    w.repo.index.commit.assert_called_once()


def test_publication_uses_assembled_bytes_and_preserves_administration(workflow, monkeypatch):
    from . import preparation

    original = preparation.apply_metadata

    def reviewed_changes(git, plan, notes):
        original(git, plan, notes)
        (git.root / PACKAGE_PATH / "Runtime/Code.cs").write_text("reviewed fix\n")

    monkeypatch.setattr(preparation, "apply_metadata", reviewed_changes)
    w = prepare_package(workflow)
    w.pull.body = "Reviewed release."
    # Later worktree edits must not change the already assembled archive.
    (w.git.root / PACKAGE_PATH / "Runtime/Code.cs").write_text("later edit\n")
    publish_fixture(w)
    unpacked = w.root / "unpacked"
    extract_package(w.package_archive, unpacked)
    distribution = Path(w.distribution.working_tree_dir)
    for path in unpacked.rglob("*"):
        installed = distribution / path.relative_to(unpacked)
        if path.is_file():
            assert installed.read_bytes() == path.read_bytes()
            assert installed.stat().st_mode & 0o111 == path.stat().st_mode & 0o111
    assert (distribution / ".github/CODEOWNERS").read_text() == "owner\n"
    assert (distribution / ".gitignore").read_text() == "*.xcframework/\n*.cs\n"
    assert not (distribution / "obsolete.txt").exists()
    assert (distribution / "Runtime/Code.cs").read_text() == "reviewed fix\n"
    w.package.create_release.assert_called_once_with("1.7.2", SIGNED, "- Fixed a crash.")
    w.distribution.git.add.assert_any_call("--all", "--force")
    w.source.create_reference.assert_any_call("tags/1.7.2", REVIEWED)
    w.push.assert_called_with(w.package, branch="main", expected_parent=MAIN, token="fake-package")
    w.source.delete_branch.assert_called_once_with("release/1.7.2", REVIEWED)


def test_integration_preserves_higher_development_metadata(workflow):
    w = prepare_package(workflow)
    development = dict(w.files)
    development[f"{PACKAGE_PATH}/package.json"] = (
        '{"version":"2.0.0","name":"new development","extra":true}'
    )
    w.commits[DEVELOP] = w.make_commit(DEVELOP, development)
    w.source.repository.get_pulls.return_value = []
    w.repo.is_dirty.return_value = True
    w.repo.head.commit = w.make_commit(LOCAL)
    integrate(w.source, source_sha=REVIEWED, version="1.7.2", source_token="fake-source")
    metadata = json.loads((w.git.root / PACKAGE_PATH / "package.json").read_text())
    assert metadata == {"version": "2.0.0", "name": "new development", "extra": True}
    assert 'AssemblyVersion("2.0.0")' in (w.git.root / ASSEMBLY_PATH).read_text()
    assert "## 1.7.2" in (w.git.root / CHANGELOG_PATH).read_text()
    w.repo.create_head.assert_any_call("release-base", REVIEWED)
    w.push.assert_called_with(
        w.source, branch="merge-back/1.7.2", expected_parent=REVIEWED,
        create_branch=True, token="fake-source",
    )
    pull = w.source.repository.create_pull.call_args.kwargs
    assert "merge commit" in pull["body"]
    assert pull["base"] == "develop" and pull["head"] == "merge-back/1.7.2"


@pytest.mark.parametrize("merged", [False, True])
def test_integration_reuses_reviewed_or_merged_pr_without_rewriting(workflow, caplog, merged):
    caplog.set_level("INFO")
    w = workflow
    pull = SimpleNamespace(
        state="closed" if merged else "open", merged=merged,
        title="Reviewed integration", body="Maintainer conflict resolution",
        html_url="https://github.invalid/fixture/source/pull/3",
    )
    w.source.repository.get_pulls.return_value = [pull]
    integrate(
        w.source, source_sha=REVIEWED, version="1.7.2", source_token="fake-source", dry_run=True
    )
    assert pull.html_url in caplog.text
    assert pull.title == "Reviewed integration" and pull.body == "Maintainer conflict resolution"
    w.clone.assert_not_called()
    w.push.assert_not_called()
    w.source.repository.create_pull.assert_not_called()


def test_partial_publication_requires_manual_recovery(workflow):
    w = prepare_package(workflow)
    w.package.create_release.side_effect = RuntimeError("Injected release failure")
    with pytest.raises(RuntimeError, match="Injected"):
        publish_fixture(w)
    assert w.refs["tags/1.7.2"] == REVIEWED
    assert w.refs["heads/release/1.7.2"] == REVIEWED
    w.source.delete_branch.assert_not_called()
    assert w.push.call_count == 2
    with pytest.raises(ValueError, match="already exists"):
        publish_fixture(w)
    assert w.push.call_count == 2


@pytest.mark.parametrize("existing", ["source_tag", "package_tag", "release"])
def test_publication_stops_on_existing_output_before_staging(workflow, existing):
    w = workflow
    if existing == "source_tag":
        w.refs["tags/1.7.2"] = REVIEWED
    elif existing == "package_tag":
        w.package.reference_sha.side_effect = None
        w.package.reference_sha.return_value = SIGNED
    else:
        w.package.release.return_value = Mock()
    with pytest.raises(ValueError, match="already exists"):
        publish(
            w.source, w.package, w.root / "missing-package.tar.gz", version="1.7.2",
            source_sha=CUT, package_token="fake-package",
        )
    w.clone.assert_not_called()
    w.push.assert_not_called()


def test_publish_and_pending_integration_dry_runs_never_write(workflow, caplog):
    caplog.set_level("INFO")
    w = prepare_package(workflow)
    w.push.reset_mock()
    w.source.create_reference.reset_mock()
    w.source.repository.create_pull.reset_mock()
    original_archive = w.package_archive.read_bytes()
    publish_fixture(w, dry_run=True)
    assert "Preparing publication of Unity SDK 1.7.2" in caplog.text
    assert "Skipping tag creation, package push and GitHub publication due to dry run." in caplog.text
    caplog.clear()
    w.source.repository.get_pulls.return_value = []
    integrate(
        w.source, version="1.7.2", source_token="fake-source", dry_run=True, source_sha=REVIEWED
    )
    assert "Preparing integration PR: merge-back/1.7.2 -> develop" in caplog.text
    assert "Skipping branch push and PR creation due to dry run." in caplog.text
    assert "Proposed PR diff:" in caplog.text
    for operations in (w.source, w.package):
        operations.create_reference.assert_not_called()
        operations.create_release.assert_not_called()
        operations.delete_branch.assert_not_called()
    w.source.repository.create_pull.assert_not_called()
    w.push.assert_not_called()
    assert w.package_archive.read_bytes() == original_archive


@pytest.mark.parametrize("use_ci_sha", [False, True])
def test_prepare_uses_local_head_unless_ci_provides_a_commit(workflow, monkeypatch, use_ci_sha):
    from . import cli

    w = workflow
    w.repo.head.commit = w.make_commit(DEVELOP)
    monkeypatch.delenv("CI_PROJECT_DIR", raising=False)
    if use_ci_sha:
        monkeypatch.setenv("CI_COMMIT_SHA", CUT)
    else:
        monkeypatch.delenv("CI_COMMIT_SHA", raising=False)
    local = Mock()
    local.head.commit.hexsha = DEVELOP
    repo = Mock(return_value=local)
    monkeypatch.setattr(cli, "Repo", repo)
    monkeypatch.setattr(cli, "GitHubOperations", Mock(return_value=w.source))
    monkeypatch.setattr(cli, "native_release_notes", Mock(return_value=""))
    monkeypatch.setattr(cli, "draft_notes", Mock(return_value=("- Fixed a crash.", ())))
    head = CUT if use_ci_sha else DEVELOP
    w.repo.head.commit = w.commits[head]
    cli.run(cli.parser().parse_args([
        "prepare", "--base-version", "1.7.1", "--release-version", "2.0.0-preview.1",
    ]))
    w.repo.create_head.assert_any_call("release-base", head)
    w.source.create_reference.assert_called_once_with("heads/release/2.0.0-preview.1", head)
    assert repo.call_count == (0 if use_ci_sha else 1)


def test_assembly_uses_ci_checkout_without_preparation_or_source_api(workflow, monkeypatch):
    from . import cli

    w = workflow
    monkeypatch.chdir(w.root)
    monkeypatch.setenv("CI_PROJECT_DIR", str(w.git.root))
    monkeypatch.setenv("CI_COMMIT_SHA", CUT)
    monkeypatch.delenv("CI_COMMIT_BRANCH", raising=False)
    repo = Mock(return_value=w.repo)
    monkeypatch.setattr(cli, "Repo", repo)
    monkeypatch.setattr(cli, "GitHubOperations", Mock(side_effect=AssertionError("Source API used")))
    monkeypatch.setattr(
        "github.Requester.Requester.requestJsonAndCheck",
        Mock(side_effect=AssertionError("GitHub API used")),
    )
    monkeypatch.setenv("DEPOT_GITHUB_RELEASES_BASE", "https://depot.example.invalid")
    response = MagicMock()
    response.__enter__.return_value = response
    response.iter_content.return_value = [w.archive.read_bytes()]
    # The native client only constructs the asset URL; no GitHub read is needed.
    client = Mock()
    client.get_repo.return_value.full_name = "DataDog/dd-sdk-ios"
    monkeypatch.setattr("dd_sdk_unity.scripts.common.github.Github", Mock(return_value=client))
    download = Mock(return_value=response)
    monkeypatch.setattr("dd_sdk_unity.release.package.requests.get", download)
    cli.run(cli.parser().parse_args(["assemble"]))
    repo.assert_called_once_with(str(w.git.root))
    w.clone.assert_not_called()
    w.source.repository.create_pull.assert_not_called()
    download.assert_called_once_with(
        "https://depot.example.invalid/DataDog/dd-sdk-ios/releases/download/"
        "1.0.0/Datadog.xcframework.zip", stream=True, timeout=180,
    )
    response.raise_for_status.assert_called_once()
    payload = w.root / "ordinary-package"
    extract_package(w.root / "package.tar.gz", payload)
    assert json.loads((payload / "package.json").read_text())["version"] == "1.7.1"
    assert (payload / "Runtime/Code.cs").read_text() == "fixed\n"


def test_unreviewed_candidate_cannot_publish(workflow):
    with pytest.raises(ValueError, match="No merged preparation PR"):
        publish(
            workflow.source, workflow.package, workflow.root / "package.tar.gz",
            version="1.7.2", source_sha=CUT, package_token="fake-package",
        )
    workflow.clone.assert_not_called()
    workflow.push.assert_not_called()


def test_cancelled_preparation_can_be_created_again_from_same_commit(workflow):
    w = workflow
    plan = prepare_plan(w.git, w.source, CUT)
    open_preparation(w.git, w.source, plan, "- Fix.", token="fake-token")
    # A cancelled PR is closed without a matching merged candidate.
    w.source.repository.get_pulls.return_value = [SimpleNamespace(merged=False)]
    with pytest.raises(ValueError, match="No merged preparation PR"):
        reviewed_pull(w.source, "release/1.7.2", CUT)
    w.refs.pop("heads/release/1.7.2")
    open_preparation(w.git, w.source, plan, "- Fix.", token="fake-token")
    assert w.source.repository.create_pull.call_count == 2
    first, second = w.source.repository.create_pull.call_args_list
    assert first.kwargs["head"] == second.kwargs["head"] == "release-prep/1.7.2"
    assert w.push.call_count == 2


def test_hotfix_plan_excludes_develop_feature(workflow):
    w = workflow
    w.source.merged_pull_numbers_since.return_value = [1, 2]
    pulls = {
        1: SimpleNamespace(
            number=1, title="fix: Correct released code", body="", merge_commit_sha=CUT
        ),
        2: SimpleNamespace(
            number=2, title="feat: New API", body="", merge_commit_sha=DEVELOP
        ),
    }
    w.source.repository.get_pull.side_effect = pulls.__getitem__
    plan = prepare_plan(w.git, w.source, CUT)
    assert plan.release.version == "1.7.2"
    assert [change.number for change in plan.release.changes] == [1]
    w.repo.iter_commits.assert_called_once_with(f"{BASE}..{CUT}")


def test_package_is_reproducible_retains_tvos_guids_and_notice(workflow):
    w = prepare_package(workflow)
    payload = w.root / "unpacked"
    extract_package(w.package_archive, payload)
    assert not (payload / "Tests").exists()
    assert not (payload / "Tests.meta").exists()
    assert (
        payload / "Plugins/iOS/DatadogCore.xcframework/tvos-arm64/symbols.dSYM/bin"
    ).read_bytes() == b"native binary fixture"
    assert not (payload / "Plugins/iOS/Unselected.xcframework").exists()
    assert (
        payload / "Editor/com.datadoghq.unity.editor.asmdef.meta"
    ).read_text() == "guid: preserved-editor-guid\n"
    assert (
        payload / "Plugins/iOS/DatadogCore.xcframework.meta"
    ).read_text() == "guid: preserved-plugin-guid\n"
    assert (payload / "NOTICE.txt").read_text() == "Committed notice\n"
    archive_package(payload, w.root / "second.tar.gz")
    assert sha256(w.package_archive) == sha256(w.root / "second.tar.gz")
    changed = w.root / "invalid.zip"
    changed.write_bytes(b"changed")
    with pytest.raises(ValueError, match="SHA-256"):
        assemble(w.git, REVIEWED, changed, w.root / "bad-payload")


def test_ai_drafting_checks_coverage_and_runs_group_draft_cleanup():
    from .changelog import classify

    changes = (
        classify("fix: Correct timestamp", "Timestamp details", number=1),
        classify("ci: Update runners", "Runner details", number=9),
    )
    groups = GroupedPRs(
        groups=[
            PRGroup(label="Timestamps", prs=[GroupedPR(number=1, title="Model-provided title")]),
            PRGroup(label="CI", prs=[GroupedPR(number=9, title="ci: Update runners")]),
        ]
    )
    draft = ChangelogEntryList(
        breaking_changes=[],
        features=[],
        fixes=[
            ChangelogEntry(text="Corrects timestamps."),
            ChangelogEntry(text="Prevents buffer overflows."),
        ],
    )
    empty = ChangelogEntryList(breaking_changes=[], features=[], fixes=[])
    prompt = Mock(side_effect=[groups, draft, empty, draft])
    links = "### Native SDK updates\n\n- [iOS release notes](https://example.invalid/native)."
    notes, returned_groups = draft_notes(changes, links, prompt)
    assert "Corrects timestamps." in notes
    assert "Prevents buffer overflows." in notes
    assert notes.endswith(links)
    assert returned_groups == tuple(groups.groups)
    assert prompt.call_count == 4
    grouping, synthesis, ci, cleanup = prompt.call_args_list
    assert "    1 fix: Correct timestamp" in grouping.args[0]
    assert "Timestamp details" not in grouping.args[0]
    assert "<title>fix: Correct timestamp</title>" in synthesis.args[0]
    assert "<body>\nTimestamp details\n</body>" in synthesis.args[0]
    assert "Model-provided title" not in synthesis.args[0]
    assert "<body>\nRunner details\n</body>" in ci.args[0]
    assert "### Fixes\n1. Corrects timestamps." in cleanup.args[0]
    assert all(links not in call.args[0] for call in prompt.call_args_list)
    for invalid in (
        GroupedPRs(groups=[]),
        GroupedPRs(groups=[PRGroup(label="Unknown", prs=[GroupedPR(number=99, title="Invented")])]),
        GroupedPRs(groups=[*groups.groups, groups.groups[0]]),
        GroupedPRs(
            groups=[
                PRGroup(
                    label="Repeated PR",
                    prs=[*groups.groups[0].prs, *groups.groups[0].prs, *groups.groups[1].prs],
                )
            ]
        ),
    ):
        with pytest.raises(ValueError, match="PR numbers do not match input"):
            draft_notes(changes, "", Mock(return_value=invalid))
    with pytest.raises(ValueError, match="empty PR group"):
        draft_notes(
            changes,
            "",
            Mock(return_value=GroupedPRs(groups=[*groups.groups, PRGroup(label="Empty", prs=[])])),
        )


@pytest.mark.parametrize("prefix", ["", "[RUM-12494]: "])
def test_changelog_skips_ordinary_chores_but_includes_breaking_chores(prefix):
    from .changelog import classify

    prompt = Mock()
    notes, groups = draft_notes((classify(prefix + "chore: Internal cleanup", number=1),), "", prompt)
    assert notes == "- Maintenance release; no significant changes."
    assert groups == ()
    prompt.assert_not_called()
    draft = ChangelogEntryList(
        breaking_changes=[ChangelogEntry(text="Removes the old API.")], features=[], fixes=[]
    )
    prompt.side_effect = [
        GroupedPRs(
            groups=[
                PRGroup(label="API", prs=[GroupedPR(number=1, title=prefix + "chore!: Remove API")])
            ]
        ),
        draft,
        draft,
    ]
    notes, _ = draft_notes((classify(prefix + "chore!: Remove API", number=1),), "", prompt)
    assert "### Breaking Changes\n\n- Removes the old API." in notes
    assert prompt.call_count == 3


def test_gateway_prompt_uses_luna_structured_responses(monkeypatch):
    from . import changelog

    draft = ChangelogEntryList(
        breaking_changes=[], features=[], fixes=[ChangelogEntry(text="Corrects timestamps.")]
    )
    response = Mock()
    response.json.return_value = {
        "status": "completed",
        "output": [
            {"type": "reasoning", "summary": []},
            {
                "type": "message",
                "content": [{"type": "output_text", "text": draft.model_dump_json()}],
            },
        ],
    }
    post = Mock(return_value=response)
    monkeypatch.setenv("AI_GATEWAY_TOKEN", "fake-token")
    monkeypatch.setattr(changelog.requests, "post", post)
    assert changelog.gateway_prompt("Draft notes.", ChangelogEntryList) == draft
    response.raise_for_status.assert_called_once()
    assert post.call_args.args == ("https://ai-gateway.us1.ddbuild.io/v1/responses",)
    request = post.call_args.kwargs
    assert request["headers"]["provider"] == "openai"
    assert request["json"]["model"] == "gpt-6-luna"
    assert request["json"]["max_output_tokens"] == 8192
    assert request["json"]["input"] == [{"role": "user", "content": "Draft notes."}]
    assert request["json"]["text"]["format"] == {
        "type": "json_schema",
        "name": "ChangelogEntryList",
        "strict": True,
        "schema": ChangelogEntryList.model_json_schema(),
    }
    response.json.return_value = {"status": "incomplete", "output": []}
    with pytest.raises(ValueError, match="incomplete"):
        changelog.gateway_prompt("Draft notes.", ChangelogEntryList)


@pytest.mark.parametrize(
    ("ios", "android", "expected"),
    [
        ("1.0.0", "1.0.0", []),
        ("1.1.0", "1.0.1", [("dd-sdk-ios", "1.1.0"), ("dd-sdk-android", "1.0.1")]),
        ("0.9.0", "1.0.0", []),
    ],
)
def test_native_notes_fetch_and_summarize_only_upgrades(monkeypatch, ios, android, expected):
    from . import changelog

    git = Mock()

    def read(sha, path):
        if sha == "base":
            assert path in (IOS_PIN_PATH, ANDROID_PIN_PATH)
            return b'{"version":"1.0.0"}'
        assert sha == "source"
        assert path == ANDROID_PIN_PATH
        return b'{"version":"1.0.1","artifacts":["dd-sdk-android-rum"]}'

    git.read.side_effect = read
    plan = Mock()
    plan.release.base_sha, plan.release.source_sha = "base", "source"
    plan.ios.version, plan.android.version = ios, android
    plan.ios.modules = ("DatadogCore", "DatadogRUM")
    upstream = Mock(
        return_value=(
            "# 2.0.0 / later\nFuture change.\n"
            "# 1.1.0 / current\nLarger buffers.\n"
            "## 1.0.1\nCorrects caching.\n"
            "# 1.0.0 / previous\nAlready shipped.\n"
        )
    )
    monkeypatch.setattr(
        changelog.GitHubOperations,
        "get_file_contents",
        lambda self, ref, path: upstream(self.repository.full_name, ref, path),
    )
    prompt = Mock(return_value=changelog.NativeNotes(entries=["Corrects native caching."]))
    notes = changelog.native_release_notes(git, plan, prompt)
    assert [call.args for call in upstream.call_args_list] == [
        (f"DataDog/{name}", f"refs/tags/{version}", "CHANGELOG.md") for name, version in expected
    ]
    assert prompt.call_count == len(expected)
    for call in prompt.call_args_list:
        data = json.loads(call.args[0].rsplit("\n", 1)[1])
        assert "Corrects caching." in data["changelog"]
        assert "Already shipped." not in data["changelog"]
        assert "Future change." not in data["changelog"]
        if data["sdk"] == "Android":
            assert "Larger buffers." not in data["changelog"]
            assert data["bundled"] == ["dd-sdk-android-rum"]
        else:
            assert data["bundled"] == ["DatadogCore", "DatadogRUM"]
    assert notes.count("  - Corrects native caching.") == len(expected)
    if ios == android == "1.0.0":
        assert notes == ""
    if ios == "0.9.0":
        assert "iOS SDK: 1.0.0 → [0.9.0]" in notes
    for name, version in expected:
        assert f"https://github.com/DataDog/{name}/releases/tag/{version}" in notes


@pytest.mark.parametrize("has_target", [True, False])
def test_native_notes_bound_the_range_without_a_baseline_heading(monkeypatch, caplog, has_target):
    from . import changelog

    git = Mock()
    git.read.return_value = b'{"version":"1.0.0"}'
    plan = Mock()
    plan.ios.version, plan.android.version = "1.1.0", "1.0.0"
    plan.ios.modules = ("DatadogCore",)
    text = "# 0.9.0\nOld fix.\n"
    if has_target:
        text = "# 1.1.0\nNew fix.\n" + text
    monkeypatch.setattr(changelog.GitHubOperations, "get_file_contents", Mock(return_value=text))
    prompt = Mock(return_value=changelog.NativeNotes(entries=["Corrects native caching."]))
    notes = changelog.native_release_notes(git, plan, prompt)
    assert "iOS SDK: 1.0.0 → [1.1.0]" in notes
    if has_target:
        prompt.assert_called_once()
        data = json.loads(prompt.call_args.args[0].rsplit("\n", 1)[1])
        assert data["changelog"] == "# 1.1.0\nNew fix."
        assert "Corrects native caching." in notes
    else:
        prompt.assert_not_called()
        assert "Skipping iOS summary" in caplog.text


def test_native_notes_fall_back_to_version_history_at_the_base(monkeypatch):
    from . import changelog

    git = Mock()

    def read(sha, path):
        assert sha == "base"
        if path in (IOS_PIN_PATH, ANDROID_PIN_PATH):
            raise KeyError(path)
        assert path == NATIVE_HISTORY_PATH
        return b"| Unity SDK | iOS SDK | Android SDK |\n|---|---|---|\n| 2.0.0 | 3.11.1 | 3.10.0 |\n| 1.7.1 | 2.30.1 | 2.26.3 |\n"

    git.read.side_effect = read
    plan = Mock()
    plan.release.base_sha = "base"
    plan.release.base_version = "1.7.1"
    plan.ios.version, plan.android.version = "2.30.1", "2.26.3"
    upstream = Mock()
    monkeypatch.setattr(
        changelog.GitHubOperations,
        "get_file_contents",
        lambda self, ref, path: upstream(self.repository.full_name, ref, path),
    )
    prompt = Mock()
    assert changelog.native_release_notes(git, plan, prompt) == ""
    upstream.assert_not_called()
    prompt.assert_not_called()
