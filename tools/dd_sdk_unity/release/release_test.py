# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Release policy and remote-write boundaries with mocked GitPython objects."""

import json
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from github.GithubException import GithubException

from dd_sdk_unity.scripts.common.git import GitOperations
from dd_sdk_unity.scripts.common.github import GitHubOperations

from . import CHANGELOG_PATH, PACKAGE_PATH
from .changelog import classify, collect_changes, merge_sections
from .cli import parser
from .release_version import select_release

BASE = "a" * 40
SOURCE = "b" * 40
SIGNED = "c" * 40


@pytest.fixture
def history(repository, make_commit):
    base = make_commit(BASE)
    files = {
        f"{PACKAGE_PATH}/package.json": '{"version":"3.0.0"}',
        CHANGELOG_PATH: "# Change Log\n\n## 3.0.0\n\n- Original.\n",
    }
    source = make_commit(SOURCE, files, parents=[base])
    repository.commit.return_value = source
    repository.head.commit = source
    repository.tags = [SimpleNamespace(name="3.0.0", commit=base)]
    return SimpleNamespace(repo=repository, base=base, files=files)


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
    change = classify(title, body, number=1)
    plan = select_release(history.repo, SOURCE, (change,))
    assert plan.version == version
    assert plan.base_sha == BASE
    assert plan.source_sha == SOURCE


@pytest.mark.parametrize(
    ("prefix", "title", "kind", "version"),
    [
        ("RUM-12494 ", "feat: Add setting", "feat", "3.1.0"),
        ("RUM-12494: ", "feat(rum)!: Sample traces by session", "feat", "3.1.0"),
        ("[RUM-12494] ", "fix(ios)!: Change behavior", "fix", "3.0.1"),
        ("[RUM-12494]: ", "chore: Update native pins", "chore", "3.0.1"),
    ],
)
def test_jira_prefixed_titles_preserve_details_and_choose_release_version(
    history, prefix, title, kind, version
):
    full_title = prefix + title
    change = classify(full_title, "Change details", number=1)
    assert change.kind == kind
    assert change.title == full_title
    assert change.body == "Change details"
    assert select_release(history.repo, SOURCE, (change,)).version == version


def test_prebumped_package_uses_explicit_base_without_rewriting_source(history, make_commit):
    history.files[f"{PACKAGE_PATH}/package.json"] = '{"version":"4.0.0"}'
    history.repo.commit.return_value = make_commit(SOURCE, history.files)
    package = Path(history.repo.working_tree_dir) / PACKAGE_PATH / "package.json"
    package.parent.mkdir(parents=True)
    package.write_text(history.files[f"{PACKAGE_PATH}/package.json"])
    change = classify("feat: Next SDK", number=1)
    with pytest.raises(ValueError, match="specify BASE_VERSION"):
        select_release(history.repo, SOURCE, (change,))
    plan = select_release(
        history.repo, SOURCE, (change,), base_version="3.0.0", release_version="4.0.0"
    )
    assert plan.base_version == "3.0.0"
    assert plan.version == "4.0.0"
    assert json.loads(package.read_text())["version"] == "4.0.0"
    history.repo.index.commit.assert_not_called()
    assert select_release(
        history.repo, SOURCE, (change,), base_version="3.0.0"
    ).version == "3.1.0"


def test_version_file_does_not_choose_highest_ancestor_tag(history, make_commit):
    history.repo.tags.append(SimpleNamespace(name="9.0.0", commit=make_commit("d" * 40)))
    assert select_release(
        history.repo, SOURCE, (classify("fix: Prevent crash", number=1),)
    ).base_version == "3.0.0"


def test_empty_inventory_is_rejected_and_unprefixed_titles_default_to_fix(history):
    with pytest.raises(ValueError, match="no releasable"):
        select_release(history.repo, SOURCE, ())
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


def test_prerelease_can_be_the_base_for_a_final_release(history, make_commit):
    history.files[f"{PACKAGE_PATH}/package.json"] = '{"version":"4.0.0-preview1"}'
    history.files[CHANGELOG_PATH] = "# Change Log\n\n## 4.0.0-preview1\n\n- Preview.\n"
    history.repo.commit.return_value = make_commit(SOURCE, history.files)
    history.repo.tags.append(SimpleNamespace(name="4.0.0-preview1", commit=history.base))
    plan = select_release(
        history.repo, SOURCE, (classify("fix: Prevent crash", number=1),), release_version="4.0.0"
    )
    assert plan.base_version == "4.0.0-preview1"
    assert plan.version == "4.0.0"


def test_change_collection_includes_stacked_prs_and_excludes_prs_merged_elsewhere(
    history, make_commit
):
    history.repo.iter_commits.return_value = [
        make_commit("c" * 40, message="fix: Bottom of the stack"),
        make_commit("d" * 40, message="feat(rum): Top of the stack"),
        make_commit(SOURCE, message="Merge pull request #8 from top"),
    ]
    github = Mock()
    github.merged_pull_numbers_since.return_value = [9, 8, 6, 7]
    pulls = {
        6: SimpleNamespace(number=6, title="fix: Hotfix", body="", merge_commit_sha="0" * 40),
        7: SimpleNamespace(
            number=7, title="fix: Bottom", body="Bottom details", merge_commit_sha=SOURCE
        ),
        8: SimpleNamespace(
            number=8, title="feat(rum): Top", body="Top details", merge_commit_sha=SOURCE
        ),
        9: SimpleNamespace(
            number=9, title="fix: Independent fix", body="Fix details", merge_commit_sha="d" * 40
        ),
    }
    github.repository.get_pull.side_effect = pulls.__getitem__
    changes = collect_changes(history.repo, github, BASE, SOURCE)
    assert [(change.number, change.kind) for change in changes] == [
        (7, "fix"), (8, "feat"), (9, "fix")
    ]
    assert [change.body for change in changes] == ["Bottom details", "Top details", "Fix details"]
    history.repo.iter_commits.assert_called_once_with(f"{BASE}..{SOURCE}")
    github.merged_pull_numbers_since.assert_called_once_with("2026-11-11")
    assert github.repository.get_pull.call_count == 4
    assert select_release(history.repo, SOURCE, changes).version == "3.1.0"


def test_empty_release_range_makes_no_github_requests(history):
    history.repo.iter_commits.return_value = []
    github = Mock()
    assert collect_changes(history.repo, github, BASE, BASE) == ()
    github.merged_pull_numbers_since.assert_not_called()
    github.repository.get_pull.assert_not_called()


def github_stub(parent):
    github = Mock(spec=GitHubOperations)
    github.reference_sha.return_value = parent
    github.repository = SimpleNamespace(full_name="example/sdk")
    return github


@pytest.mark.parametrize("parents", [[BASE, "d" * 40], ["d" * 40]])
def test_push_rejects_invalid_parents_before_invoking_signer(history, make_commit, monkeypatch, parents):
    history.repo.head.commit.parents = [make_commit(sha) for sha in parents]
    invoke = Mock()
    monkeypatch.setattr("dd_sdk_unity.scripts.common.git.subprocess.run", invoke)
    with pytest.raises(ValueError, match="single-parent"):
        GitOperations(history.repo).push(github_stub(BASE), branch="develop", expected_parent=BASE)
    invoke.assert_not_called()


def test_push_rejects_moved_head_before_invoking_signer(history, monkeypatch):
    invoke = Mock()
    monkeypatch.setattr("dd_sdk_unity.scripts.common.git.subprocess.run", invoke)
    with pytest.raises(ValueError, match="moved"):
        GitOperations(history.repo).push(
            github_stub("d" * 40), branch="develop", expected_parent=BASE
        )
    invoke.assert_not_called()


@pytest.mark.parametrize("mismatch", [None, "ref", "tree", "parent"])
def test_signed_push_verifies_remote_sha_and_tree(history, monkeypatch, mismatch):
    github = github_stub(BASE)
    github.reference_sha.side_effect = [BASE, "d" * 40 if mismatch == "ref" else SIGNED]
    github.git_commit.return_value = SimpleNamespace(
        tree=SimpleNamespace(sha="d" * 40 if mismatch == "tree" else SOURCE),
        parents=[SimpleNamespace(sha="d" * 40 if mismatch == "parent" else BASE)],
    )
    invoke = Mock(return_value=SimpleNamespace(stdout=SIGNED + "\n"))
    monkeypatch.setattr("dd_sdk_unity.scripts.common.git.subprocess.run", invoke)
    operations = GitOperations(history.repo)
    if mismatch:
        with pytest.raises(RuntimeError, match="Remote branch|Signed commit"):
            operations.push(github, branch="develop", expected_parent=BASE, token="not-a-real-token")
    else:
        assert operations.push(
            github, branch="develop", expected_parent=BASE, token="not-a-real-token"
        ) == SIGNED
    assert "--head-sha" in invoke.call_args.args[0]
    assert invoke.call_args.kwargs["env"]["HEADLESS_TOKEN"] == "not-a-real-token"


def test_github_ref_errors(monkeypatch):
    repository = Mock()
    repository.get_git_ref.return_value.object = SimpleNamespace(type="commit", sha=BASE)
    client = Mock()
    client.get_repo.return_value = repository
    monkeypatch.setattr("dd_sdk_unity.scripts.common.github.Github", Mock(return_value=client))
    github = GitHubOperations("example/sdk")
    repository.get_git_ref.side_effect = GithubException(404, {"message": "Not found"})
    assert github.reference_sha("tags/3.0.1") is None
    repository.get_git_ref.side_effect = GithubException(403, {"message": "Forbidden"})
    with pytest.raises(GithubException):
        github.reference_sha("tags/3.0.1")
