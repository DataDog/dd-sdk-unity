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
from unittest.mock import MagicMock, Mock

import pytest
from git import GitCommandError, Repo
from github import GithubException

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
    extract_package,
    sha256,
)
from .preparation import (
    open_preparation,
    prepare_plan,
    reviewed_pull,
)
from .publish import publish
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

    def create_git_release(self, *, tag, name, message, target_commitish):
        if tag not in self.git.tags:
            self.git.create_tag(tag, ref=target_commitish)
        release = SimpleNamespace(
            tag_name=tag,
            body=message,
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
            zipped.writestr(prefix + name, b"native binary fixture")
        zipped.writestr("Datadog.xcframework/Unselected.xcframework/NotBundled", b"unused")
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
        source_repo, f"{PACKAGE_PATH}/Runtime/Code.cs", "fixed\n", "fix: Prevent crash (#1)"
    ).hexsha
    package_repo = initialize(tmp_path / "package-remote", "main")
    commit(package_repo, ".github/CODEOWNERS", "owner\n", "chore: Administration")
    commit(package_repo, ".gitignore", "*.xcframework/\n*.cs\n", "chore: Ignore build files")
    commit(package_repo, "package.json", '{"version":"1.7.1"}', "chore: Original package")
    package_repo.create_tag("1.7.1")
    source_api = RepositoryFixture(source_repo, "source")
    source_api.pulls[1] = SimpleNamespace(
        number=1,
        title="fix: Prevent crash",
        body="Corrects an SDK crash.",
        state="closed",
        merged=True,
        merge_commit_sha=cut,
        base=SimpleNamespace(ref="develop"),
        head=SimpleNamespace(ref="crash-fix"),
    )
    package_api = RepositoryFixture(package_repo, "package")
    repositories = {source_api.full_name: source_api, package_api.full_name: package_api}
    client = Mock()
    client.get_repo.side_effect = lambda name: repositories.get(name, Mock(full_name=name))
    monkeypatch.setattr("dd_sdk_unity.scripts.common.github.Github", Mock(return_value=client))
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
        source=GitHubOperations(source_api.full_name),
        package=GitHubOperations(package_api.full_name),
        archive=archive,
        pushes=pushes,
        cut=cut,
    )


def prepare_and_merge(w):
    git = GitOperations.clone(w.source.repository.clone_url, w.root / "prepare", revision=w.cut)
    plan = prepare_plan(git, w.source, w.cut)
    pull = open_preparation(git, w.source, plan, "- Fixed a crash.", token="fake-token")
    pull = w.source.repository.pulls[pull.number]
    remote = w.source.repository.git
    remote.heads[pull.base.ref].checkout()
    remote.git.merge("--no-ff", pull.head.ref, "-m", pull.title)
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
    publish(
        w.source,
        w.package,
        w.package_archive,
        version=w.plan.release.version,
        source_sha=w.source_sha,
        package_token="fake-package",
        dry_run=dry_run,
    )
    return w.package.reference_sha(f"tags/{w.plan.release.version}")


@pytest.mark.parametrize(
    ("ios_version", "android_version"),
    [("1.0.0", "1.0.0"), ("1.1.0", "1.0.0"), ("1.0.0", "1.1.0"), ("1.1.0", "1.1.0")],
)
def test_preparation_pr_reuses_generated_groups(workflow, ios_version, android_version):
    w = workflow
    git = GitOperations.clone(w.source.repository.clone_url, w.root / "prepare", revision=w.cut)
    plan = prepare_plan(git, w.source, w.cut)
    change = plan.release.changes[0]
    release = plan.release.model_copy(
        update={
            "changes": (
                change.model_copy(update={"number": 8}),
                change.model_copy(update={"number": 9, "title": "ci: Update runners"}),
            )
        }
    )
    plan = plan.model_copy(
        update={
            "release": release,
            "ios": plan.ios.model_copy(update={"version": ios_version}),
            "android": plan.android.model_copy(update={"version": android_version}),
        }
    )
    pull = open_preparation(
        git,
        w.source,
        plan,
        "- Fixed a crash.",
        token="fake-token",
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
    assert pull.head.ref == "release-prep/1.7.2"
    assert pull.base.ref == "release/1.7.2"


def test_complete_workflow_uses_assembled_bytes_and_preserves_develop(workflow, caplog):
    caplog.set_level("INFO")
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
    package_sha = publish_fixture(w)
    assert package_sha == w.package.reference_sha("heads/main")
    assert w.source.reference_sha(f"heads/{w.pull.base.ref}") is None
    assert w.source.reference_sha("tags/1.7.2") == w.source_sha
    assert package_sha != w.source_sha
    with tarfile.open(w.package_archive) as archive:
        for member in archive.getmembers():
            if member.isdir():
                continue
            blob = w.package.repository.git.commit(package_sha).tree / member.name.removeprefix(
                "./"
            )
            if member.issym():
                assert blob.mode == 0o120000
                assert blob.data_stream.read().decode() == member.linkname
            else:
                assert bool(blob.mode & 0o111) == bool(member.mode & 0o111)
                stream = archive.extractfile(member)
                assert stream is not None
                assert blob.data_stream.read() == stream.read()
    assert (
        w.package.repository.git.commit(package_sha).tree / ".github/CODEOWNERS"
    ).data_stream.read() == b"owner\n"
    remote = w.source.repository.git
    remote.heads.develop.checkout()
    commit(
        remote,
        f"{PACKAGE_PATH}/package.json",
        '{"version":"2.0.0","name":"new development","extra":true}',
        "feat: Next API",
    )
    integrate(w.source, source_sha=w.source_sha, version="1.7.2", source_token="fake-source")
    incoming = remote.heads["merge-back/1.7.2"].commit
    assert remote.is_ancestor(w.source_sha, incoming)
    metadata = json.loads((incoming.tree / PACKAGE_PATH / "package.json").data_stream.read())
    assert metadata == {"version": "2.0.0", "name": "new development", "extra": True}
    assert b'AssemblyVersion("2.0.0")' in (incoming.tree / ASSEMBLY_PATH).data_stream.read()
    assert len(incoming.parents) == 1
    pull = next(
        p for p in w.source.repository.pulls.values() if p.head.ref.startswith("merge-back/")
    )
    assert "merge commit" in pull.body
    pull.title, pull.body = "Reviewed integration", "Maintainer conflict resolution"
    remote.heads["merge-back/1.7.2"].checkout()
    edited = commit(
        remote, f"{PACKAGE_PATH}/Runtime/Resolution.cs", "resolved\n", "fix: Resolve conflict"
    )
    pull.head.sha = edited.hexsha
    remote.heads.develop.checkout()
    caplog.clear()
    integrate(
        w.source,
        source_sha=w.source_sha,
        version="1.7.2",
        source_token="fake-source",
        dry_run=True,
    )
    preview = caplog.text
    assert f"Reuse integration PR: {pull.html_url}" in preview
    assert pull.title == "Reviewed integration" and pull.body == "Maintainer conflict resolution"
    assert remote.heads["merge-back/1.7.2"].commit.hexsha == edited.hexsha
    pushes_before = len(w.pushes)
    with pytest.raises(ValueError, match="already exists"):
        publish_fixture(w)
    assert len(w.pushes) == pushes_before


def test_partial_publication_requires_manual_recovery(workflow, monkeypatch):
    w = prepare_and_merge(workflow)
    monkeypatch.setattr(
        w.package.repository,
        "create_git_release",
        Mock(side_effect=RuntimeError("Injected release failure")),
    )
    with pytest.raises(RuntimeError, match="Injected"):
        publish_fixture(w)
    assert w.source.reference_sha("tags/1.7.2") == w.source_sha
    assert w.source.reference_sha(f"heads/{w.pull.base.ref}") == w.source_sha
    assert w.package.release("1.7.2") is None
    package_pushes = [p for p in w.pushes if p[0] == w.package.repository.full_name]
    assert len(package_pushes) == 1
    with pytest.raises(ValueError, match="already exists"):
        publish_fixture(w)
    assert len(w.pushes) == 2  # One preparation push and one package push.


@pytest.mark.parametrize("existing", ["source_tag", "package_tag", "release"])
def test_publication_stops_on_existing_output_before_staging(workflow, monkeypatch, existing):
    w = workflow
    if existing == "source_tag":
        w.source.repository.git.create_tag("1.7.2")
    elif existing == "package_tag":
        w.package.repository.git.create_tag("1.7.2")
    else:
        w.package.repository.create_git_release(
            tag="1.7.2", name="1.7.2", message="Existing release", target_commitish="HEAD"
        )
        w.package.repository.git.delete_tag("1.7.2")
    monkeypatch.setattr(GitOperations, "clone", Mock(side_effect=AssertionError("Staging started")))
    with pytest.raises(ValueError, match="already exists"):
        publish(
            w.source,
            w.package,
            w.root / "missing-package.tar.gz",
            version="1.7.2",
            source_sha=w.cut,
            package_token="fake-package",
        )
    assert w.pushes == []


def test_publish_and_pending_integration_dry_runs_never_write(workflow, monkeypatch, caplog):
    caplog.set_level("INFO")
    w = prepare_and_merge(workflow)
    for operations in (w.source, w.package):
        for name in (
            "create_reference",
            "create_release",
            "delete_branch",
        ):
            monkeypatch.setattr(operations, name, Mock(side_effect=AssertionError("remote write")))
    monkeypatch.setattr(
        w.source.repository, "create_pull", Mock(side_effect=AssertionError("remote write"))
    )
    monkeypatch.setattr(GitOperations, "push", Mock(side_effect=AssertionError("signer invoked")))
    caplog.clear()
    original_archive = w.package_archive.read_bytes()
    assert publish_fixture(w, dry_run=True) is None
    publication_log = caplog.text
    assert "Preparing publication of Unity SDK 1.7.2" in publication_log
    assert (
        "Skipping tag creation, package push and GitHub publication due to dry run."
        in publication_log
    )
    caplog.clear()
    integrate(
        w.source,
        version="1.7.2",
        source_token="fake-source",
        dry_run=True,
        source_sha=w.source_sha,
    )
    integration_log = caplog.text
    assert "Preparing integration PR: merge-back/1.7.2 -> develop" in integration_log
    assert "Skipping branch push and PR creation due to dry run." in integration_log
    assert "Proposed PR diff:" in integration_log
    assert w.package_archive.read_bytes() == original_archive


def test_assembly_uses_ci_checkout_without_preparation_or_source_api(workflow, monkeypatch):
    from . import cli

    w = workflow
    existing_pulls = dict(w.source.repository.pulls)
    monkeypatch.chdir(w.root)
    monkeypatch.setenv("CI_PROJECT_DIR", w.source.repository.git.working_tree_dir)
    monkeypatch.setenv("CI_COMMIT_SHA", w.cut)
    monkeypatch.delenv("CI_COMMIT_BRANCH", raising=False)
    monkeypatch.setattr(cli, "GitHubOperations", Mock(side_effect=AssertionError("Source API client used")))
    monkeypatch.setattr(
        "github.Requester.Requester.requestJsonAndCheck",
        Mock(side_effect=AssertionError("GitHub API used")),
    )
    monkeypatch.setattr(GitOperations, "clone", Mock(side_effect=AssertionError("Source cloned")))
    output = w.root / "package.tar.gz"
    monkeypatch.setenv("DEPOT_GITHUB_RELEASES_BASE", "https://depot.example.invalid")
    response = MagicMock()
    response.__enter__.return_value = response
    response.iter_content.return_value = [w.archive.read_bytes()]
    download = Mock(return_value=response)
    monkeypatch.setattr("dd_sdk_unity.release.package.requests.get", download)
    cli.run(cli.parser().parse_args(["assemble"]))
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
    assert w.source.repository.pulls == existing_pulls
    assert w.source.repository.git.head.commit.hexsha == w.cut


def test_unreviewed_candidate_cannot_publish(workflow):
    with pytest.raises(ValueError, match="No merged preparation PR"):
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
    plan = prepare_plan(git, w.source, w.cut)
    first = open_preparation(git, w.source, plan, "- Fix.", token="fake-token")
    first = w.source.repository.pulls[first.number]
    first.state = "closed"
    with pytest.raises(ValueError, match="No merged preparation PR"):
        reviewed_pull(w.source, first.base.ref, w.cut)
    w.source.repository.git.delete_head(first.head.ref, first.base.ref, force=True)
    git = GitOperations.clone(w.source.repository.clone_url, w.root / "second", revision=w.cut)
    second = open_preparation(git, w.source, plan, "- Fix.", token="fake-token")
    assert second.number != first.number
    assert second.head.ref == first.head.ref


def test_hotfix_from_tag_excludes_develop_feature_and_merges_release_ancestry(workflow, caplog):
    caplog.set_level("INFO")
    w = workflow
    remote = w.source.repository.git
    feature = commit(
        remote, f"{PACKAGE_PATH}/Runtime/Feature.cs", "new API\n", "feat: Add new API (#2)"
    )
    w.source.repository.pulls[2] = SimpleNamespace(
        number=2,
        title="feat: Add new API",
        body="Adds a new SDK API.",
        state="closed",
        merged=True,
        merge_commit_sha=feature.hexsha,
        base=SimpleNamespace(ref="develop"),
        head=SimpleNamespace(ref="new-api"),
    )
    base = remote.create_head("published-base", remote.tags["1.7.1"].commit)
    remote.create_head("hotfix", base).checkout()
    w.cut = commit(
        remote, f"{PACKAGE_PATH}/Runtime/Code.cs", "hotfix\n", "fix: Correct released code (#1)"
    ).hexsha
    prepare_and_merge(w)
    assert w.plan.release.version == "1.7.2"
    assert not any(change.kind == "feat" for change in w.plan.release.changes)
    publish_fixture(w)
    integrate(
        w.source,
        version="1.7.2",
        source_token="fake-source",
        dry_run=True,
        source_sha=w.source_sha,
    )
    preview = caplog.text
    assert "Runtime/Code.cs" in preview
    assert "+hotfix" in preview
    integrate(w.source, source_sha=w.source_sha, version="1.7.2", source_token="fake-source")
    remote.heads.develop.checkout()
    # The two independently edited lines need a normal maintainer conflict resolution.
    try:
        remote.git.merge("--no-ff", "merge-back/1.7.2", "-m", "chore(release): Integrate 1.7.2")
    except GitCommandError:
        assert remote.index.unmerged_blobs()
        (Path(remote.working_tree_dir) / PACKAGE_PATH / "Runtime/Code.cs").write_text("hotfix\n")
        remote.git.add("--all")
        remote.git.commit("-m", "chore(release): Integrate 1.7.2")
    assert remote.is_ancestor(w.source_sha, remote.head.commit)
    assert (Path(remote.working_tree_dir) / PACKAGE_PATH / "Runtime/Feature.cs").is_file()
    pull = next(
        p for p in w.source.repository.pulls.values() if p.head.ref.startswith("merge-back/")
    )
    pull.merged, pull.state, pull.merge_commit_sha = True, "closed", remote.head.commit.hexsha
    # GitHub's automatic source-branch deletion handles both merged PR heads.
    remote.delete_head(pull.head.ref, w.pull.head.ref, force=True)
    integrate(w.source, source_sha=w.source_sha, version="1.7.2", source_token="fake-source")
    assert "release/1.7.2" not in remote.heads and "merge-back/1.7.2" not in remote.heads
    next_plan = prepare_plan(GitOperations(remote), w.source, remote.head.commit.hexsha)
    assert next_plan.release.base_version == "1.7.2"
    assert next_plan.release.version == "1.8.0"


def test_publication_uses_reviewed_source_changes(workflow, monkeypatch):
    from . import preparation

    original = preparation.apply_metadata

    def reviewed_changes(git, plan, notes):
        original(git, plan, notes)
        (git.root / PACKAGE_PATH / "Runtime/Code.cs").write_text("reviewed fix\n")

    monkeypatch.setattr(preparation, "apply_metadata", reviewed_changes)
    w = prepare_and_merge(workflow)
    w.pull.body = "Reviewed release."
    package_sha = publish_fixture(w)
    tree = w.package.repository.git.commit(package_sha).tree
    assert (tree / "Runtime/Code.cs").data_stream.read() == b"reviewed fix\n"
    assert w.package.repository.get_release(w.plan.release.version).body == "- Fixed a crash."


def test_package_is_reproducible_retains_tvos_guids_and_notice(workflow):
    w = prepare_and_merge(workflow)
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
        assemble(
            GitOperations(w.source.repository.git), w.source_sha, changed, w.root / "bad-payload"
        )


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


def test_changelog_skips_ordinary_chores_but_includes_breaking_chores():
    from .changelog import classify

    prompt = Mock()
    notes, groups = draft_notes((classify("chore: Internal cleanup", number=1),), "", prompt)
    assert notes == "- Maintenance release; no significant changes."
    assert groups == ()
    prompt.assert_not_called()
    draft = ChangelogEntryList(
        breaking_changes=[ChangelogEntry(text="Removes the old API.")], features=[], fixes=[]
    )
    prompt.side_effect = [
        GroupedPRs(
            groups=[PRGroup(label="API", prs=[GroupedPR(number=1, title="chore!: Remove API")])]
        ),
        draft,
        draft,
    ]
    notes, _ = draft_notes((classify("chore!: Remove API", number=1),), "", prompt)
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
        ("1.1.0", "1.0.0", [("dd-sdk-ios", "1.1.0")]),
        ("1.0.0", "1.0.1", [("dd-sdk-android", "1.0.1")]),
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
