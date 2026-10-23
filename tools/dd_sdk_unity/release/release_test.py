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

from . import CHANGELOG_PATH, PACKAGE_PATH
from .changelog import classify, collect_changes, merge_native_history, merge_sections
from .git_ops import GitOperations
from .github_ops import GitHubOperations
from .models import CompletedRelease
from .release_version import merge_package_version, select_release

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
    completed = CompletedRelease(version="3.0.0", source_sha=base.hexsha, package_sha="f" * 40)
    return repo, completed


def direct_change(repo, title="fix: Prevent crash", body=""):
    c = commit(repo, "code.cs", "changed", f"{title}\n\n{body}")
    return classify(title, body, identifier=f"commit:{c.hexsha}")


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
    repo, completed = history
    change = direct_change(repo, title, body)
    plan = select_release(repo, repo.head.commit.hexsha, completed, (change,))
    assert plan.version == version
    assert plan.base_sha == completed.source_sha


def test_prebumped_package_uses_explicit_base_without_rewriting_source(history):
    repo, completed = history
    c = commit(repo, f"{PACKAGE_PATH}/package.json", '{"version":"4.0.0"}', "feat: Next SDK")
    change = classify("feat: Next SDK", identifier="commit:" + c.hexsha)
    with pytest.raises(ValueError, match="specify BASE_VERSION"):
        select_release(repo, c.hexsha, completed, (change,))
    plan = select_release(
        repo, c.hexsha, completed, (change,), base_version="3.0.0", release_version="4.0.0"
    )
    assert (plan.declared_version, plan.base_version, plan.inferred_version, plan.version) == (
        "4.0.0",
        "3.0.0",
        "3.1.0",
        "4.0.0",
    )
    assert repo.working_tree_dir
    assert (
        json.loads((Path(repo.working_tree_dir) / PACKAGE_PATH / "package.json").read_text())[
            "version"
        ]
        == "4.0.0"
    )
    assert (
        select_release(repo, c.hexsha, completed, (change,), base_version="3.0.0").version
        == "3.1.0"
    )


def test_baseline_override_does_not_allow_an_old_maintenance_line(history):
    repo, _ = history
    change = direct_change(repo)
    newer = CompletedRelease(
        version="3.0.1", source_sha=repo.head.commit.hexsha, package_sha="e" * 40
    )
    with pytest.raises(ValueError, match="latest completed release"):
        select_release(repo, repo.head.commit.hexsha, newer, (change,), base_version="3.0.0")


def test_version_file_does_not_choose_highest_ancestor_tag(history):
    repo, completed = history
    change = direct_change(repo)
    repo.create_tag("9.0.0")
    assert (
        select_release(repo, repo.head.commit.hexsha, completed, (change,)).base_version == "3.0.0"
    )


def test_unintegrated_release_code_is_rejected(history):
    repo, completed = history
    original = repo.head.commit
    change = direct_change(repo)
    release = repo.create_head("release-code", original.hexsha)
    release.checkout()
    other = commit(repo, "release-only.cs", "unintegrated", "fix: Release only")
    repo.delete_tag("3.0.0")
    repo.create_tag("3.0.0")
    repo.heads.develop.checkout()
    mapped = completed.model_copy(update={"source_sha": other.hexsha})
    with pytest.raises(ValueError, match="not integrated"):
        select_release(repo, repo.head.commit.hexsha, mapped, (change,))


def test_empty_inventory_is_rejected_and_unprefixed_titles_default_to_fix(history):
    repo, completed = history
    direct_change(repo)
    with pytest.raises(ValueError, match="no releasable"):
        select_release(repo, repo.head.commit.hexsha, completed, ())
    title = "A title without classification"
    change = classify(title, identifier="historical")
    assert change.kind == "fix" and change.title == title


def test_changelog_order_and_published_content_protection():
    text = "# Change Log\n\n## 3.1.0\n\n- New.\n\n## 3.0.0\n\n- Old.\n"
    merged = merge_sections(text, {"3.0.1": "- Patch."})
    assert merged.index("## 3.1.0") < merged.index("## 3.0.1") < merged.index("## 3.0.0")
    assert merge_sections(merged, {"3.0.1": "- Patch."}) == merged
    with pytest.raises(ValueError, match="published"):
        merge_sections(merged, {"3.1.0": "- Rewritten."})
    with pytest.raises(ValueError, match="legacy Unreleased"):
        merge_sections("# Change Log\n\n## Unreleased\n- Pending.\n", {"3.0.1": "- Patch."})


def test_native_history_orders_semantically_and_preserves_existing_rows():
    table = "| Unity SDK | iOS SDK | Android SDK |\n|---|---|---|\n| 3.10.0 | 4.0.0 | 5.0.0 |\n| 3.9.0 | 3.0.0 | 4.0.0 |\n"
    merged = merge_native_history(table, {"3.9.1": ("3.0.0", "4.0.0")})
    assert merged.index("3.10.0") < merged.index("3.9.1") < merged.index("3.9.0")
    with pytest.raises(ValueError, match="published native"):
        merge_native_history(merged, {"3.10.0": ("99.0.0", "5.0.0")})


def test_package_integration_keeps_higher_version_and_other_development_fields():
    development = '{"version":"3.10.0","dependencies":{"new":"1.0.0"},"displayName":"Current"}\n'
    assert merge_package_version(development, "3.9.1") == development
    assert merge_package_version(development, "3.10.0") == development
    updated = json.loads(merge_package_version(development, "3.11.0"))
    assert updated == {**json.loads(development), "version": "3.11.0"}


def test_merge_history_keeps_pr_changes_once_and_does_not_push_merges(history):
    repo, completed = history
    feature = repo.create_head("feature", repo.head.reference)
    feature.checkout()
    feature_commit = commit(repo, "feature.cs", "feature", "feat: A feature")
    repo.heads.develop.checkout()
    direct = commit(repo, "fix.cs", "fix", "fix: Independent fix")
    repo.git.merge("--no-ff", "--no-gpg-sign", feature.name, "-m", "Merge feature")
    merged = repo.head.commit
    pull = SimpleNamespace(
        number=8, title="feat: A feature", body="", merge_commit_sha=merged.hexsha
    )
    github = Mock()
    github.merged_pulls.side_effect = lambda sha: (
        [pull] if sha in {feature_commit.hexsha, merged.hexsha} else []
    )
    changes = collect_changes(repo, github, completed.source_sha, merged.hexsha)
    assert len(changes) == 2
    assert {change.identifier for change in changes} == {"pr:8", f"commit:{direct.hexsha}"}
    assert select_release(repo, merged.hexsha, completed, changes).version == "3.1.0"
    with pytest.raises(ValueError, match="single-parent"):
        GitOperations(repo).push(
            github_stub(completed.source_sha),
            branch="develop",
            expected_parent=completed.source_sha,
        )


def test_metadata_release_tail_requires_the_exact_audited_sha(history, monkeypatch):
    from . import release_version

    repo, _ = history
    base = commit(
        repo, CHANGELOG_PATH, "# Change Log\n\n## 1.7.1\n\n- Published.\n", "chore: Release history"
    )
    tail_branch = repo.create_head("legacy-release", repo.head.reference)
    tail_branch.checkout()
    tagged = commit(
        repo,
        f"{PACKAGE_PATH}/package.json",
        '{"version":"1.7.1"}',
        "chore(release): Record version",
    )
    repo.create_tag("1.7.1")
    repo.heads.develop.checkout()
    change = direct_change(repo)
    record = CompletedRelease(version="1.7.1", source_sha=tagged.hexsha, package_sha="e" * 40)
    with pytest.raises(ValueError, match="not integrated"):
        select_release(repo, repo.head.commit.hexsha, record, (change,), base_version="1.7.1")
    monkeypatch.setattr(release_version, "LEGACY_BASE_SHA", tagged.hexsha)
    plan = select_release(repo, repo.head.commit.hexsha, record, (change,), base_version="1.7.1")
    assert plan.version == "1.7.2"
    assert repo.is_ancestor(base, repo.head.commit)


def test_change_collection_deduplicates_pr_commits_and_squash_commits(history):
    repo, completed = history
    commit(repo, "first.cs", "one", "fix: Implementation detail")
    second = commit(repo, "second.cs", "two", "fix: More implementation")
    pull = SimpleNamespace(
        number=8, title="feat(rum): Feature", body="Details", merge_commit_sha=second.hexsha
    )
    github = Mock()
    github.merged_pulls.return_value = [pull]
    changes = collect_changes(repo, github, completed.source_sha, second.hexsha)
    assert len(changes) == 1
    assert changes[0].kind == "feat"
    assert changes[0].identifier == "pr:8"


def test_unmapped_direct_fix_remains_visible(history):
    repo, completed = history
    change = direct_change(repo)
    github = Mock()
    github.merged_pulls.return_value = []
    changes = collect_changes(repo, github, completed.source_sha, repo.head.commit.hexsha)
    assert changes == (change,)


def test_release_pr_with_sdk_changes_is_not_all_bookkeeping(history):
    repo, completed = history
    commit(repo, f"{PACKAGE_PATH}/package.json", '{"version":"3.0.1"}', "chore: Metadata")
    code = commit(repo, "fix.cs", "fix", "fix: SDK fix")
    pull = SimpleNamespace(
        number=9, title="chore(release): Release changes", body="", merge_commit_sha=code.hexsha
    )
    github = Mock()
    github.merged_pulls.return_value = [pull]
    changes = collect_changes(repo, github, completed.source_sha, code.hexsha)
    assert len(changes) == 1 and changes[0].identifier == "pr:9"
    assert not changes[0].bookkeeping


def test_merge_back_alone_does_not_manufacture_another_patch(history):
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
        "--no-ff", "--no-gpg-sign", fix_branch.name, "-m", "chore(release): Integrate 3.0.1"
    )
    merged = repo.head.commit
    github = Mock()
    github.merged_pulls.return_value = [
        SimpleNamespace(
            number=10,
            title="chore(release): Integrate 3.0.1",
            body="",
            merge_commit_sha=merged.hexsha,
        )
    ]
    changes = collect_changes(repo, github, released.hexsha, merged.hexsha)
    assert len(changes) == 1 and changes[0].bookkeeping
    record = CompletedRelease(version="3.0.1", source_sha=released.hexsha, package_sha="e" * 40)
    with pytest.raises(ValueError, match="no releasable"):
        select_release(repo, merged.hexsha, record, changes)


def github_stub(parent, *, read_only=False):
    github = Mock(spec=GitHubOperations)
    github.reference_sha.return_value = parent
    github.read_only = read_only
    github.repository = SimpleNamespace(full_name="example/sdk")
    return github


def test_push_rejects_moved_head_before_invoking_signer(history, monkeypatch):
    repo, completed = history
    direct_change(repo)
    invoke = Mock(side_effect=AssertionError("Do not sign against a moved remote"))
    monkeypatch.setattr("dd_sdk_unity.release.git_ops.subprocess.run", invoke)
    with pytest.raises(ValueError, match="moved"):
        GitOperations(repo).push(
            github_stub("d" * 40), branch="develop", expected_parent=completed.source_sha
        )


def test_signed_push_verifies_real_remote_sha_and_tree(history, monkeypatch):
    repo, completed = history
    direct_change(repo)
    local = repo.head.commit
    signed = repo.index.commit(
        "Signed copy",
        parent_commits=local.parents,
        head=False,
        author=Actor("Signer", "signer@example.invalid"),
        committer=AUTHOR,
    )
    github = github_stub(completed.source_sha)
    github.reference_sha.side_effect = [completed.source_sha, signed.hexsha]
    github.git_commit.return_value = SimpleNamespace(
        raw_data={"verification": {"verified": True}},
        tree=SimpleNamespace(sha=signed.tree.hexsha),
        parents=[SimpleNamespace(sha=completed.source_sha)],
    )
    invoke = Mock(return_value=SimpleNamespace(stdout=signed.hexsha + "\n"))
    monkeypatch.setattr("dd_sdk_unity.release.git_ops.subprocess.run", invoke)
    operations = GitOperations(repo)
    result = operations.push(
        github, branch="develop", expected_parent=completed.source_sha, token="not-a-real-token"
    )
    assert result == signed.hexsha != local.hexsha
    assert "--head-sha" in invoke.call_args.args[0]
    assert invoke.call_args.kwargs["env"]["HEADLESS_TOKEN"] == "not-a-real-token"


def test_github_reference_conflicts_and_read_only_boundary():
    repository = Mock()
    repository.get_git_ref.return_value.object = SimpleNamespace(type="commit", sha="a" * 40)
    github = GitHubOperations(repository, read_only=True)
    with pytest.raises(ValueError, match="Conflicting"):
        github.ensure_reference("tags/3.0.1", "b" * 40)
    repository.get_git_ref.side_effect = GithubException(404, {"message": "Not found"})
    with pytest.raises(RuntimeError, match="read-only"):
        github.ensure_reference("heads/new", "a" * 40)
    repository.create_git_ref.assert_not_called()
    repository.get_git_ref.side_effect = GithubException(403, {"message": "Forbidden"})
    with pytest.raises(GithubException):
        github.reference_sha("tags/3.0.1")
