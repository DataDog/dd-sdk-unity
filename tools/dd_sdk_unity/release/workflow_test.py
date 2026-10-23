# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Exercise two-repository release handoffs locally; GitHub and signing stay fake."""

import json
import plistlib
import tarfile
import zipfile
from pathlib import Path
from types import SimpleNamespace
from typing import cast
from unittest.mock import MagicMock, Mock

import pytest
from git import GitCommandError, Repo
from github import GithubException
from github.Repository import Repository

from . import (
    ANDROID_PIN_PATH,
    ASSEMBLY_PATH,
    CHANGELOG_PATH,
    IOS_PIN_PATH,
    NATIVE_HISTORY_PATH,
    PACKAGE_PATH,
    release_state,
)
from .changelog import NoteDraft, NoteGroups, draft_notes
from .git_ops import GitOperations
from .github_ops import GitHubOperations
from .integration import integrate
from .package import (
    archive_package,
    assemble,
    assemble_package,
    extract_package,
    sha256,
)
from .preparation import (
    CandidateNotApproved,
    check_metadata,
    open_preparation,
    preparation_base,
    prepare_plan,
    reviewed_pull,
)
from .publish import completed_publication, publish
from .release_test import AUTHOR, commit


class RepositoryFixture:
    """Only the public PyGithub methods used by the workflow, backed by local Git."""

    def __init__(self, repo, name):
        self.git = repo
        self.full_name = f"fixture/{name}"
        self.owner = SimpleNamespace(login="fixture")
        self.clone_url = str(repo.working_tree_dir)
        self.html_url = f"https://github.invalid/{self.full_name}"
        self.pulls = {}
        self.releases = {}

    def get_git_ref(self, name):
        prefix, value = name.split("/", 1)
        refs = self.git.heads if prefix == "heads" else self.git.tags
        if value not in refs:
            raise GithubException(404, {})
        reference = refs[value]
        return SimpleNamespace(
            object=SimpleNamespace(type="commit", sha=reference.commit.hexsha),
            delete=lambda: self.git.delete_head(value, force=True),
        )

    def create_git_ref(self, name, sha):
        if name.startswith("refs/heads/"):
            self.git.create_head(name.removeprefix("refs/heads/"), sha)
        else:
            self.git.create_tag(name.removeprefix("refs/tags/"), ref=sha)

    def get_git_commit(self, sha):
        value = self.git.commit(sha)
        return SimpleNamespace(
            tree=SimpleNamespace(sha=value.tree.hexsha),
            parents=[SimpleNamespace(sha=parent.hexsha) for parent in value.parents],
            raw_data={"verification": {"verified": True}},
        )

    def get_commit(self, sha):
        return SimpleNamespace(get_pulls=list)

    def get_pull(self, number):
        return self.pulls[number]

    def get_pulls(self, state, base, head=None):
        return [
            pull
            for pull in self.pulls.values()
            if pull.base.ref == base
            and (state == "all" or pull.state == state)
            and (head is None or pull.head.ref == head.split(":")[-1])
        ]

    def create_pull(self, *, base, head, title, body):
        number = len(self.pulls) + 1
        pull = SimpleNamespace(
            number=number,
            state="open",
            merged=False,
            merge_commit_sha=None,
            base=SimpleNamespace(ref=base, repo=self),
            head=SimpleNamespace(ref=head, sha=self.git.heads[head].commit.hexsha, repo=self),
            title=title,
            body=body,
            html_url=f"{self.html_url}/pull/{number}",
        )
        self.pulls[number] = pull
        return pull

    def get_release(self, version):
        if version not in self.releases:
            raise GithubException(404, {})
        return self.releases[version]

    def get_releases(self):
        return list(self.releases.values())

    def create_git_release(self, *, tag, name, message, target_commitish):
        if tag not in self.git.tags:
            self.git.create_tag(tag, ref=target_commitish)
        release = SimpleNamespace(
            tag_name=tag,
            body=message,
            draft=False,
            prerelease=False,
        )
        self.releases[tag] = release
        return release


def initialize(path, branch):
    repo = Repo.init(path, initial_branch=branch)
    with repo.config_writer() as config:
        config.set_value("core", "fsmonitor", False)
        config.set_value("commit", "gpgsign", False)
        config.set_value("tag", "gpgsign", False)
        config.set_value("user", "name", AUTHOR.name)
        config.set_value("user", "email", AUTHOR.email)
    return repo


@pytest.fixture
def workflow(tmp_path, monkeypatch):
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
            entry = zipfile.ZipInfo(prefix + name)
            entry.external_attr = 0o100755 << 16
            zipped.writestr(entry, b"native binary fixture")
        entry = zipfile.ZipInfo(prefix + "ios-arm64/DatadogCore.framework/Alias")
        entry.external_attr = 0o120777 << 16
        zipped.writestr(entry, b"DatadogCore")
    source_repo = initialize(tmp_path / "source-remote", "develop")
    documents = {
        f"{PACKAGE_PATH}/package.json": json.dumps({"version": "1.7.1", "name": "fixture"}),
        ASSEMBLY_PATH: '[assembly: AssemblyVersion("1.7.1")]\n',
        CHANGELOG_PATH: "# Change Log\n\n## 1.7.1\n\n- Original notes.\n",
        NATIVE_HISTORY_PATH: "| Unity SDK | iOS SDK | Android SDK |\n|---|---|---|\n| 1.7.1 | 1.0.0 | 1.0.0 |\n",
        IOS_PIN_PATH: json.dumps(
            {"version": "1.0.0", "sha256": sha256(archive), "modules": ["DatadogCore"]}
        ),
        ANDROID_PIN_PATH: '{"version":"1.0.0","artifacts":["rum"]}',
        f"{PACKAGE_PATH}/Runtime/Code.cs": "initial\n",
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
    for name, text in documents.items():
        target = Path(source_repo.working_tree_dir) / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)
    source_repo.git.add("--all")
    source_repo.index.commit("chore: Initial metadata", author=AUTHOR, committer=AUTHOR)
    source_repo.create_tag("1.7.1")
    cut = commit(
        source_repo, f"{PACKAGE_PATH}/Runtime/Code.cs", "fixed\n", "fix: Prevent crash"
    ).hexsha
    package_repo = initialize(tmp_path / "package-remote", "main")
    commit(package_repo, ".github/CODEOWNERS", "owner\n", "chore: Administration")
    commit(package_repo, ".gitignore", "*.xcframework/\n*.cs\n", "chore: Ignore build files")
    commit(package_repo, "package.json", '{"version":"1.7.1"}', "chore: Original package")
    package_repo.create_tag("1.7.1")
    source_api = RepositoryFixture(source_repo, "source")
    package_api = RepositoryFixture(package_repo, "package")
    package_api.create_git_release(
        tag="1.7.1",
        name="1.7.1",
        message="Original notes.",
        target_commitish=package_repo.head.commit.hexsha,
    )
    pushes = []

    def push(git, github, *, branch, expected_parent, create_branch=False, token=None):
        assert token
        assert github.reference_sha(f"heads/{branch}") == (
            None if create_branch else expected_parent
        )
        # Model headless's different signed SHA. Transfer objects locally, never push GitHub.
        signed = git.repository.index.commit(
            str(git.repository.head.commit.message) + "\nSigned fixture\n",
            parent_commits=[git.repository.commit(expected_parent)],
            author=AUTHOR,
            committer=AUTHOR,
        ).hexsha
        remote = github.repository.git
        remote.git.fetch(str(git.root), signed)
        if branch in remote.heads:
            remote.heads[branch].set_commit(signed)
        else:
            remote.create_head(branch, signed)
        pushes.append((github.repository.full_name, branch, signed))
        return signed

    monkeypatch.setattr(GitOperations, "push", push)
    return SimpleNamespace(
        root=tmp_path,
        source=GitHubOperations(cast(Repository, source_api)),
        package=GitHubOperations(cast(Repository, package_api)),
        archive=archive,
        pushes=pushes,
        cut=cut,
    )


def prepare_and_merge(w, *, legacy=False):
    if legacy:
        text = w.source.repository.git.commit(w.cut).tree / CHANGELOG_PATH
        text = (
            text.data_stream.read()
            .decode()
            .replace("## 1.7.1", "## Unreleased\n\n- Working note.\n\n## 1.7.1")
        )
        w.cut = commit(w.source.repository.git, CHANGELOG_PATH, text, "fix: Document fix").hexsha
    git = GitOperations.clone(w.source.repository.clone_url, w.root / "prepare", revision=w.cut)
    plan = prepare_plan(git, w.source, w.package, w.cut)
    pull = open_preparation(git, w.source, w.package, plan, "- Fixed a crash.", token="fake-token")
    pull = w.source.repository.get_pull(pull.number)
    remote = w.source.repository.git
    remote.heads[pull.base.ref].checkout()
    remote.git.merge(
        "--no-ff", pull.head.ref, "-m", f"chore(release): Prepare {plan.release.version}"
    )
    pull.state, pull.merged, pull.merge_commit_sha = "closed", True, remote.head.commit.hexsha
    w.plan, w.pull = plan, pull
    w.source_sha = remote.head.commit.hexsha
    remote.heads.develop.checkout()
    w.package_archive = w.root / "package.tar.gz"
    git = GitOperations.clone(
        w.source.repository.clone_url, w.root / "assemble", revision=w.source_sha
    )
    assemble_package(git, w.source_sha, w.archive, w.package_archive)
    return w


def publish_fixture(w, *, dry_run=False):
    return publish(
        w.source,
        w.package,
        w.package_archive,
        version=w.plan.release.version,
        source_sha=w.source_sha,
        package_token="fake-package",
        dry_run=dry_run,
    )


def test_complete_workflow_uses_assembled_bytes_and_preserves_develop(workflow, capsys):
    w = workflow
    commit(
        w.source.repository.git, f"{PACKAGE_PATH}/Runtime/Later.cs", "later\n", "feat: Later API"
    )
    prepare_and_merge(w)
    assert w.plan.release.source_sha == w.cut
    assert w.plan.release.version == "1.7.2"
    assert (
        f"{PACKAGE_PATH}/Runtime/Later.cs" not in w.source.repository.git.commit(w.source_sha).tree
    )
    record = publish_fixture(w)
    assert record.source_sha == w.source_sha
    assert w.source.reference_sha(f"heads/{w.pull.base.ref}") is None
    assert w.source.reference_sha("tags/1.7.2") == record.source_sha
    assert record.package_sha != record.source_sha
    with tarfile.open(w.package_archive) as archive:
        for member in archive.getmembers():
            if member.isdir():
                continue
            blob = w.package.repository.git.commit(
                record.package_sha
            ).tree / member.name.removeprefix("./")
            if member.issym():
                assert blob.mode == 0o120000
                assert blob.data_stream.read().decode() == member.linkname
            else:
                assert bool(blob.mode & 0o111) == bool(member.mode & 0o111)
                stream = archive.extractfile(member)
                assert stream is not None
                assert blob.data_stream.read() == stream.read()
    assert (
        w.package.repository.git.commit(record.package_sha).tree / ".github/CODEOWNERS"
    ).data_stream.read() == b"owner\n"
    w.package.repository.get_release("1.7.2").body = "Corrected release description."
    assert release_state.completed_release(w.source, w.package, "1.7.2") == record
    assert release_state.latest_completed(w.source, w.package).version == "1.7.2"
    remote = w.source.repository.git
    remote.heads.develop.checkout()
    commit(
        remote,
        f"{PACKAGE_PATH}/package.json",
        '{"version":"2.0.0","name":"new development","extra":true}',
        "feat: Next API",
    )
    integrate(w.source, w.package, version="1.7.2", source_token="fake-source")
    incoming = remote.heads["merge-back/1.7.2"].commit
    assert remote.is_ancestor(record.source_sha, incoming)
    metadata = json.loads((incoming.tree / PACKAGE_PATH / "package.json").data_stream.read())
    assert metadata == {"version": "2.0.0", "name": "new development", "extra": True}
    assert len(incoming.parents) == 1
    pull = next(p for p in w.source.repository.pulls.values() if p.base.ref == "develop")
    assert "merge commit" in pull.body
    pull.title, pull.body = "Reviewed integration", "Maintainer conflict resolution"
    remote.heads["merge-back/1.7.2"].checkout()
    edited = commit(
        remote, f"{PACKAGE_PATH}/Runtime/Resolution.cs", "resolved\n", "fix: Resolve conflict"
    )
    pull.head.sha = edited.hexsha
    remote.heads.develop.checkout()
    capsys.readouterr()
    integrate(
        w.source,
        w.package,
        version="1.7.2",
        source_token="fake-source",
        dry_run=True,
    )
    preview = capsys.readouterr().out
    assert f"Reuse integration PR: {pull.html_url}" in preview
    assert pull.title == "Reviewed integration" and pull.body == "Maintainer conflict resolution"
    assert remote.heads["merge-back/1.7.2"].commit.hexsha == edited.hexsha
    pushes_before = len(w.pushes)
    w.source.repository.git.delete_head(w.pull.head.ref, force=True)
    assert publish_fixture(w) == record
    assert len(w.pushes) == pushes_before
    capsys.readouterr()
    assert completed_publication(w.source, w.package, "1.7.2") == record
    status = capsys.readouterr().out
    assert "already published" in status
    assert record.source_sha in status and record.package_sha in status


@pytest.mark.parametrize("failure", ["before_package_push", "before_release", "before_cleanup"])
def test_partial_publication_resumes_without_another_package_commit(workflow, monkeypatch, failure):
    w = prepare_and_merge(workflow)
    if failure == "before_package_push":
        original = GitOperations.push

        def fail_once(git, *args, **kwargs):
            monkeypatch.setattr(GitOperations, "push", original)
            raise RuntimeError("Injected push failure")

        monkeypatch.setattr(GitOperations, "push", fail_once)
    elif failure == "before_release":
        original = w.package.repository.create_git_release

        def fail_once(**kwargs):
            monkeypatch.setattr(w.package.repository, "create_git_release", original)
            raise RuntimeError("Injected release failure")

        monkeypatch.setattr(w.package.repository, "create_git_release", fail_once)
    else:
        original = w.source.delete_branch

        def fail_once(*args, **kwargs):
            monkeypatch.setattr(w.source, "delete_branch", original)
            raise RuntimeError("Injected branch cleanup failure")

        monkeypatch.setattr(w.source, "delete_branch", fail_once)
    with pytest.raises(RuntimeError, match="Injected"):
        publish_fixture(w)
    if failure == "before_release":
        w.package_archive = w.root / "reassembled.tar.gz"
        git = GitOperations.clone(
            w.source.repository.clone_url, w.root / "reassembled-source", revision=w.source_sha
        )
        assemble_package(git, w.source_sha, w.archive, w.package_archive)
    record = publish_fixture(w)
    assert record.version == "1.7.2"
    assert w.source.reference_sha(f"heads/{w.pull.base.ref}") is None
    package_pushes = [p for p in w.pushes if p[0] == w.package.repository.full_name]
    assert len(package_pushes) == 1
    assert w.package.reference_sha("tags/1.7.2") == record.package_sha
    assert w.package.repository.get_release("1.7.2").tag_name == record.version


def test_publish_and_pending_integration_dry_runs_never_write(workflow, monkeypatch, capsys):
    w = prepare_and_merge(workflow)
    for operations in (w.source, w.package):
        operations.read_only = True
        for name in (
            "ensure_reference",
            "ensure_release",
            "delete_branch",
        ):
            monkeypatch.setattr(operations, name, Mock(side_effect=AssertionError("remote write")))
    monkeypatch.setattr(
        w.source.repository, "create_pull", Mock(side_effect=AssertionError("remote write"))
    )
    monkeypatch.setattr(GitOperations, "push", Mock(side_effect=AssertionError("signer invoked")))
    capsys.readouterr()
    original_archive = w.package_archive.read_bytes()
    assert publish_fixture(w, dry_run=True) is None
    publication_log = capsys.readouterr().out
    assert "Preparing publication of Unity SDK 1.7.2" in publication_log
    assert (
        "Skipping tag creation, package push and GitHub publication due to dry run."
        in publication_log
    )
    integrate(
        w.source,
        w.package,
        version="1.7.2",
        source_token="fake-source",
        dry_run=True,
        source_sha=w.source_sha,
    )
    integration_log = capsys.readouterr().out
    assert "Preparing integration PR: merge-back/1.7.2 -> develop" in integration_log
    assert "Skipping branch push and PR creation due to dry run." in integration_log
    assert "Proposed PR diff:" in integration_log
    assert w.package_archive.read_bytes() == original_archive


@pytest.mark.parametrize("download_native", [False, True])
def test_assembly_uses_ci_checkout_without_preparation_or_source_api(
    workflow, monkeypatch, download_native
):
    from . import cli

    w = workflow
    monkeypatch.setenv("CI_PROJECT_DIR", w.source.repository.git.working_tree_dir)
    monkeypatch.setenv("CI_COMMIT_SHA", w.cut)
    monkeypatch.delenv("CI_COMMIT_BRANCH", raising=False)
    monkeypatch.setattr(cli, "Github", Mock(side_effect=AssertionError("GitHub client used")))
    monkeypatch.setattr(GitOperations, "clone", Mock(side_effect=AssertionError("Source cloned")))
    output = w.root / "ordinary-package.tar.gz"
    args = ["assemble", "--output", str(output)]
    if download_native:
        monkeypatch.setenv("DEPOT_GITHUB_RELEASES_BASE", "https://depot.example.invalid")
        response = MagicMock()
        response.__enter__.return_value = response
        response.iter_content.return_value = [w.archive.read_bytes()]
        download = Mock(return_value=response)
        monkeypatch.setattr("dd_sdk_unity.release.package.requests.get", download)
    else:
        args.extend(["--native-archive", str(w.archive)])
    cli.run(cli.parser().parse_args(args))
    if download_native:
        download.assert_called_once_with(
            "https://depot.example.invalid/DataDog/dd-sdk-ios/releases/download/"
            "1.0.0/Datadog.xcframework.zip",
            stream=True,
            timeout=180,
        )
        response.raise_for_status.assert_called_once()
    payload = w.root / "ordinary-package"
    extract_package(output, payload)
    assert json.loads((payload / "package.json").read_text())["version"] == "1.7.1"
    assert (payload / "Runtime/Code.cs").read_text() == "fixed\n"
    assert w.source.repository.pulls == {}
    assert w.source.repository.git.head.commit.hexsha == w.cut


def test_unreviewed_candidate_cannot_publish(workflow):
    with pytest.raises(CandidateNotApproved):
        publish(
            workflow.source,
            workflow.package,
            workflow.root / "package.tar.gz",
            version="1.7.2",
            source_sha=workflow.cut,
            package_token="fake-package",
        )


def test_cancelled_preparation_can_be_created_again_from_same_commit(workflow, monkeypatch):
    w = workflow
    monkeypatch.setenv("GIT_AUTHOR_DATE", "2026-10-22T12:00:00+0000")
    monkeypatch.setenv("GIT_COMMITTER_DATE", "2026-10-22T12:00:00+0000")
    git = GitOperations.clone(w.source.repository.clone_url, w.root / "first", revision=w.cut)
    plan = prepare_plan(git, w.source, w.package, w.cut)
    first = open_preparation(git, w.source, w.package, plan, "- Fix.", token="fake-token")
    first = w.source.repository.get_pull(first.number)
    first.state = "closed"
    with pytest.raises(CandidateNotApproved):
        reviewed_pull(w.source, first.base.ref, w.cut)
    w.source.repository.git.delete_head(first.head.ref, first.base.ref, force=True)
    git = GitOperations.clone(w.source.repository.clone_url, w.root / "second", revision=w.cut)
    second = open_preparation(git, w.source, w.package, plan, "- Fix.", token="fake-token")
    assert second.number != first.number
    assert second.head.ref == first.head.ref


def test_hotfix_from_tag_excludes_develop_feature_and_merges_release_ancestry(workflow, capsys):
    w = workflow
    remote = w.source.repository.git
    commit(remote, f"{PACKAGE_PATH}/Runtime/Feature.cs", "new API\n", "feat: Add new API")
    base = remote.create_head("published-base", remote.tags["1.7.1"].commit)
    remote.create_head("hotfix", base).checkout()
    w.cut = commit(
        remote, f"{PACKAGE_PATH}/Runtime/Code.cs", "hotfix\n", "fix: Correct released code"
    ).hexsha
    prepare_and_merge(w)
    assert w.plan.release.version == "1.7.2"
    assert not any(change.kind == "feat" for change in w.plan.release.changes)
    record = publish_fixture(w)
    integrate(
        w.source,
        w.package,
        version="1.7.2",
        source_token="fake-source",
        dry_run=True,
        source_sha=w.source_sha,
    )
    preview = capsys.readouterr().out
    assert "Runtime/Code.cs" in preview
    assert "+hotfix" in preview
    integrate(w.source, w.package, version="1.7.2", source_token="fake-source")
    remote.heads.develop.checkout()
    # The two independently edited lines need a normal maintainer conflict resolution.
    try:
        remote.git.merge("--no-ff", "merge-back/1.7.2", "-m", "chore(release): Integrate 1.7.2")
    except GitCommandError:
        assert remote.index.unmerged_blobs()
        (Path(remote.working_tree_dir) / PACKAGE_PATH / "Runtime/Code.cs").write_text("hotfix\n")
        remote.git.add("--all")
        remote.git.commit("-m", "chore(release): Integrate 1.7.2")
    assert remote.is_ancestor(record.source_sha, remote.head.commit)
    assert (Path(remote.working_tree_dir) / PACKAGE_PATH / "Runtime/Feature.cs").is_file()
    pull = next(p for p in w.source.repository.pulls.values() if p.base.ref == "develop")
    pull.merged, pull.state, pull.merge_commit_sha = True, "closed", remote.head.commit.hexsha
    # GitHub's automatic source-branch deletion handles both merged PR heads.
    remote.delete_head(pull.head.ref, w.pull.head.ref, force=True)
    integrate(w.source, w.package, version="1.7.2", source_token="fake-source")
    assert "release-prep/1.7.2" not in remote.heads and "merge-back/1.7.2" not in remote.heads
    next_plan = prepare_plan(GitOperations(remote), w.source, w.package, remote.head.commit.hexsha)
    assert next_plan.release.base_version == "1.7.2"
    assert next_plan.release.version == "1.8.0"


def test_review_baseline_is_required_and_metadata_is_restricted(workflow):
    w = prepare_and_merge(workflow)
    pull = w.source.repository.get_pull(w.pull.number)
    pull.body = pull.body.replace("unity-release-base", "removed-baseline")
    with pytest.raises(ValueError, match="selected baseline"):
        preparation_base(pull)
    remote = w.source.repository.git
    commit(remote, f"{PACKAGE_PATH}/Runtime/Code.cs", "unreviewed code\n", "fix: Sneak in code")
    with pytest.raises(ValueError, match="SDK"):
        check_metadata(GitOperations(remote), w.cut, remote.head.commit.hexsha, "1.7.2")


def test_package_is_reproducible_retains_tvos_modes_guids_and_notice(workflow):
    w = prepare_and_merge(workflow)
    payload = w.root / "unpacked"
    extract_package(w.package_archive, payload)
    assert not (payload / "Tests").exists()
    assert not (payload / "Tests.meta").exists()
    assert (
        payload / "Plugins/iOS/DatadogCore.xcframework/tvos-arm64/symbols.dSYM/bin"
    ).stat().st_mode & 0o111
    alias = payload / "Plugins/iOS/DatadogCore.xcframework/ios-arm64/DatadogCore.framework/Alias"
    assert alias.is_symlink() and alias.readlink() == Path("DatadogCore")
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
        assemble(
            GitOperations(w.source.repository.git), w.source_sha, changed, w.root / "bad-payload"
        )


def test_legacy_working_notes_move_to_reviewed_section(workflow):
    w = prepare_and_merge(workflow, legacy=True)
    assert w.plan.legacy_notes == "- Working note."
    assert (
        "Unreleased"
        not in w.source.repository.git.commit(w.source_sha)
        .tree.join(CHANGELOG_PATH)
        .data_stream.read()
        .decode()
    )
    publish_fixture(w)
    integrate(w.source, w.package, version="1.7.2", source_token="fake-source")


def test_ai_drafting_checks_coverage_and_runs_group_draft_cleanup():
    from .changelog import classify

    changes = (classify("fix: Correct timestamp", identifier="commit:one"),)
    answers = [
        NoteGroups(groups=[["commit:one", "commit:one"], ["commit:one"]]),
        NoteDraft(breaking_changes=[], features=[], fixes=["Corrects timestamps."]),
        NoteDraft(breaking_changes=[], features=[], fixes=["Corrects timestamps."]),
    ]
    prompt = Mock(side_effect=answers)
    links = "### Native SDK updates\n\n- [iOS release notes](https://example.invalid/native)."
    notes = draft_notes(changes, "", links, prompt)
    assert "Corrects timestamps." in notes
    assert notes.endswith(links)
    assert all(links not in call.args[0] for call in prompt.call_args_list)
    assert prompt.call_count == 3
    with pytest.raises(ValueError, match="cover the captured"):
        draft_notes(changes, "", "", Mock(return_value=NoteGroups(groups=[])))
    with pytest.raises(ValueError, match="cover the captured"):
        draft_notes(changes, "", "", Mock(return_value=NoteGroups(groups=[["invented"]])))


def test_native_release_links_include_only_changed_versions():
    from .changelog import native_release_links

    git = Mock()
    git.read.return_value = (
        b"| Unity SDK | iOS SDK | Android SDK |\n|---|---|---|\n| 1.7.1 | 2.0.0 | 1.0.0 |\n"
    )
    plan = Mock()
    plan.release.base_version = "1.7.1"
    plan.ios.version, plan.android.version = "1.9.0", "1.0.0"
    links = native_release_links(git, plan)
    assert "iOS SDK: 2.0.0 → [1.9.0]" in links
    assert "https://github.com/DataDog/dd-sdk-ios/releases/tag/1.9.0" in links
    assert "Android" not in links
