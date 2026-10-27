# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Release policy and remote-write boundaries, using real local Git history."""

import json
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from git import Actor, Repo
from github.GithubException import GithubException

from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.github import GitHubOperations

from . import CHANGELOG_PATH, PACKAGE_PATH
from .changelog import classify, collect_changes, merge_sections
from .cli import parser
from .release_version import select_release

AUTHOR = Actor("Release test", "release-test@example.invalid")


def commit(repo: Repo, path: str, text: str, message: str):
    assert repo.working_tree_dir
    target = Path(repo.working_tree_dir) / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text)
    repo.index.add([path])
    return repo.index.commit(message, author=AUTHOR, committer=AUTHOR)


@pytest.fixture
def history(tmp_path):
    repo = Repo.init(tmp_path / "source")
    with repo.config_writer() as config:
        config.set_value("core", "fsmonitor", False)
        config.set_value("tag", "gpgSign", False)
        config.set_value("commit", "gpgSign", False)
        config.set_value("user", "name", AUTHOR.name)
        config.set_value("user", "email", AUTHOR.email)
    commit(repo, f"{PACKAGE_PATH}/package.json", '{"version":"3.0.0"}', "chore: Initial package")
    base = commit(
        repo, CHANGELOG_PATH, "# Change Log\n\n## 3.0.0\n\n- Original.\n", "chore: Initial notes"
    )
    repo.create_head("develop", repo.head.reference).checkout()
    repo.create_tag("3.0.0")
    return repo, base


def direct_change(repo, title="fix: Prevent crash", body=""):
    commit(repo, "code.cs", "changed", f"{title}\n\n{body}")
    return classify(title, body, number=1)


@pytest.mark.parametrize(
    ("title", "body", "version"),
    [
        ("fix: Prevent crash", "", "3.0.1"),
        ("feat(rum): Add setting", "", "3.1.0"),
        ("fix!: Change behavior", "BREAKING CHANGE: reviewed", "3.0.1"),
        ("feat!: Add setting", "", "3.1.0"),
        ("chore: Update native pins", "", "3.0.1"),
        ("FFL-2510: Thread allocationKey into FlagDetails.Metadata", "", "3.0.1"),
        ("BREAKING: Change behavior", "", "3.0.1"),
    ],
)
def test_bump_policy_has_no_automatic_major(history, title, body, version):
    repo, base = history
    change = direct_change(repo, title, body)
    plan = select_release(repo, repo.head.commit.hexsha, (change,))
    assert plan.version == version
    assert plan.base_sha == base.hexsha


def test_prebumped_package_uses_explicit_base_without_rewriting_source(history):
    repo, _ = history
    c = commit(repo, f"{PACKAGE_PATH}/package.json", '{"version":"4.0.0"}', "feat: Next SDK")
    change = classify("feat: Next SDK", number=1)
    with pytest.raises(ValueError, match="specify BASE_VERSION"):
        select_release(repo, c.hexsha, (change,))
    plan = select_release(repo, c.hexsha, (change,), base_version="3.0.0", release_version="4.0.0")
    assert plan.base_version == "3.0.0"
    assert plan.version == "4.0.0"
    assert repo.working_tree_dir
    assert (
        json.loads((Path(repo.working_tree_dir) / PACKAGE_PATH / "package.json").read_text())[
            "version"
        ]
        == "4.0.0"
    )
    assert select_release(repo, c.hexsha, (change,), base_version="3.0.0").version == "3.1.0"


def test_version_file_does_not_choose_highest_ancestor_tag(history):
    repo, _ = history
    change = direct_change(repo)
    repo.create_tag("9.0.0")
    assert select_release(repo, repo.head.commit.hexsha, (change,)).base_version == "3.0.0"


def test_empty_inventory_is_rejected_and_unprefixed_titles_default_to_fix(history):
    repo, _ = history
    direct_change(repo)
    with pytest.raises(ValueError, match="no releasable"):
        select_release(repo, repo.head.commit.hexsha, ())
    title = "A title without classification"
    change = classify(title, number=1)
    assert change.kind == "fix" and change.title == title


def test_changelog_order_and_published_content_protection():
    text = "# Change Log\n\n## 3.1.0\n\n- New.\n\n## 3.0.0\n\n- Old.\n"
    merged = merge_sections(text, {"3.0.1": "- Patch."})
    assert merged.index("## 3.1.0") < merged.index("## 3.0.1") < merged.index("## 3.0.0")
    assert merge_sections(merged, {"3.0.1": "- Patch."}) == merged
    with pytest.raises(ValueError, match="published"):
        merge_sections(merged, {"3.1.0": "- Rewritten."})


def test_prerelease_cli_and_changelog_ordering():
    args = parser().parse_args([
        "prepare", "--base-version", "1.7.1", "--release-version", "2.0.0-preview1",
    ])
    assert args.base_version == "1.7.1"
    assert args.release_version == "2.0.0-preview1"
    versions = ["2.0.0", "2.0.0-preview.10", "2.0.0-preview.2", "1.7.1"]
    text = merge_sections("# Change Log\n", {version: f"- {version}" for version in reversed(versions)})
    positions = [text.index(f"## {version}\n") for version in versions]
    assert positions == sorted(positions)


def test_prerelease_can_be_the_base_for_a_final_release(history):
    repo, _ = history
    commit(repo, f"{PACKAGE_PATH}/package.json", '{"version":"4.0.0-preview1"}', "chore: Preview")
    commit(repo, CHANGELOG_PATH, "# Change Log\n\n## 4.0.0-preview1\n\n- Preview.\n", "chore: Notes")
    repo.create_tag("4.0.0-preview1")
    change = direct_change(repo)
    plan = select_release(repo, repo.head.commit.hexsha, (change,), release_version="4.0.0")
    assert plan.base_version == "4.0.0-preview1"
    assert plan.version == "4.0.0"


def test_merge_history_keeps_pr_changes_once_and_does_not_push_merges(history):
    repo, base = history
    feature = repo.create_head("feature", repo.head.reference)
    feature.checkout()
    commit(repo, "feature.cs", "feature", "feat: A feature")
    repo.heads.develop.checkout()
    commit(repo, "fix.cs", "fix", "fix: Independent fix (#9)")
    repo.git.merge(
        "--no-ff", "--no-gpg-sign", feature.name, "-m", "Merge pull request #8 from feature"
    )
    merged = repo.head.commit
    github = Mock()
    pulls = {
        8: SimpleNamespace(number=8, title="feat: A feature", body="Feature details"),
        9: SimpleNamespace(number=9, title="fix: Independent fix", body="Fix details"),
    }
    github.repository.get_pull.side_effect = pulls.__getitem__
    changes = collect_changes(repo, github, base.hexsha, merged.hexsha)
    assert [change.number for change in changes] == [8, 9]
    assert [change.body for change in changes] == ["Feature details", "Fix details"]
    assert github.repository.get_pull.call_count == 2
    assert select_release(repo, merged.hexsha, changes).version == "3.1.0"
    with pytest.raises(ValueError, match="single-parent"):
        GitOperations(repo).push(
            github_stub(base.hexsha), branch="develop", expected_parent=base.hexsha
        )


def test_change_collection_fetches_each_pr_once_for_merge_and_squash_subjects(history):
    repo, base = history
    commit(repo, "first.cs", "one", "fix: Implementation detail")
    commit(repo, "second.cs", "two", "fix: More implementation (#8)")
    commit(repo, "third.cs", "three", "Merge pull request #8 from feature")
    github = Mock()
    github.repository.get_pull.return_value = SimpleNamespace(
        number=8, title="feat(rum): Feature", body="Details"
    )
    changes = collect_changes(repo, github, base.hexsha, repo.head.commit.hexsha)
    assert len(changes) == 1
    assert changes[0].kind == "feat"
    assert changes[0].number == 8
    github.repository.get_pull.assert_called_once_with(8)


def test_commits_without_pr_numbers_make_no_github_requests(history):
    repo, base = history
    direct_change(repo)
    github = Mock()
    assert collect_changes(repo, github, base.hexsha, repo.head.commit.hexsha) == ()
    github.repository.get_pull.assert_not_called()


def test_merge_back_alone_can_propose_another_patch(history):
    repo, _ = history
    fix_branch = repo.create_head("fix-branch", repo.head.reference)
    fix_branch.checkout()
    commit(repo, "fix.cs", "fix", "fix: Published SDK fix")
    commit(repo, f"{PACKAGE_PATH}/package.json", '{"version":"3.0.1"}', "chore(release): Version")
    released = commit(
        repo,
        CHANGELOG_PATH,
        "# Change Log\n\n## 3.0.1\n\n- Fix.\n\n## 3.0.0\n\n- Original.\n",
        "chore(release): Notes",
    )
    repo.create_tag("3.0.1")
    repo.heads.develop.checkout()
    repo.git.merge(
        "--no-ff",
        "--no-gpg-sign",
        fix_branch.name,
        "-m",
        "Merge pull request #10 from merge-back/3.0.1",
    )
    merged = repo.head.commit
    github = Mock()
    github.repository.get_pull.return_value = SimpleNamespace(
        number=10,
        title="chore(release): Integrate 3.0.1",
        body="",
        merge_commit_sha=merged.hexsha,
    )
    changes = collect_changes(repo, github, released.hexsha, merged.hexsha)
    assert len(changes) == 1 and changes[0].kind == "chore"
    assert select_release(repo, merged.hexsha, changes).version == "3.0.2"


def github_stub(parent):
    github = Mock(spec=GitHubOperations)
    github.reference_sha.return_value = parent
    github.repository = SimpleNamespace(full_name="example/sdk")
    return github


def test_push_rejects_moved_head_before_invoking_signer(history, monkeypatch):
    repo, base = history
    direct_change(repo)
    invoke = Mock(side_effect=AssertionError("Do not sign against a moved remote"))
    monkeypatch.setattr("dd_sdk_unity.scripts.common.git.subprocess.run", invoke)
    with pytest.raises(ValueError, match="moved"):
        GitOperations(repo).push(
            github_stub("d" * 40), branch="develop", expected_parent=base.hexsha
        )


def test_signed_push_verifies_real_remote_sha_and_tree(history, monkeypatch):
    repo, base = history
    direct_change(repo)
    local = repo.head.commit
    signed = repo.index.commit(
        "Signed copy",
        parent_commits=local.parents,
        head=False,
        author=Actor("Signer", "signer@example.invalid"),
        committer=AUTHOR,
    )
    github = github_stub(base.hexsha)
    github.reference_sha.side_effect = [base.hexsha, signed.hexsha]
    github.git_commit.return_value = SimpleNamespace(
        tree=SimpleNamespace(sha=signed.tree.hexsha),
        parents=[SimpleNamespace(sha=base.hexsha)],
    )
    invoke = Mock(return_value=SimpleNamespace(stdout=signed.hexsha + "\n"))
    monkeypatch.setattr("dd_sdk_unity.scripts.common.git.subprocess.run", invoke)
    operations = GitOperations(repo)
    result = operations.push(
        github, branch="develop", expected_parent=base.hexsha, token="not-a-real-token"
    )
    assert result == signed.hexsha != local.hexsha
    assert "--head-sha" in invoke.call_args.args[0]
    assert invoke.call_args.kwargs["env"]["HEADLESS_TOKEN"] == "not-a-real-token"


def test_github_ref_errors(monkeypatch):
    repository = Mock()
    repository.get_git_ref.return_value.object = SimpleNamespace(type="commit", sha="a" * 40)
    client = Mock()
    client.get_repo.return_value = repository
    monkeypatch.setattr("dd_sdk_unity.scripts.common.github.Github", Mock(return_value=client))
    github = GitHubOperations("example/sdk")
    repository.get_git_ref.side_effect = GithubException(404, {"message": "Not found"})
    assert github.reference_sha("tags/3.0.1") is None
    repository.get_git_ref.side_effect = GithubException(403, {"message": "Forbidden"})
    with pytest.raises(GithubException):
        github.reference_sha("tags/3.0.1")
