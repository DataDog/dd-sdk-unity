# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Release metadata preparation and review approval checks."""

import json
from types import SimpleNamespace
from unittest.mock import Mock

import pytest

from dd_sdk_unity.scripts.common.versions.sdkversions import SdkVersionTable

from . import ASSEMBLY_PATH, CHANGELOG_PATH, NATIVE_HISTORY_PATH, PACKAGE_PATH, preparation
from .changelog_prompts import GroupedPR, PRGroup

SOURCE = "a" * 40


@pytest.fixture
def plan():
    return Mock(
        release=Mock(
            version="1.7.2",
            base_version="1.7.1",
            source_sha=SOURCE,
            changes=(Mock(number=1), Mock(number=2)),
        ),
        ios=Mock(version="3.21.0"),
        android=Mock(version="3.16.0"),
    )


def test_open_preparation_uses_the_source_commit_and_existing_groups(plan, monkeypatch):
    git = Mock()
    source = Mock()
    source.reference_sha.return_value = None
    source.repository.create_pull.return_value.html_url = "https://github.invalid/pull/1"
    apply = Mock()
    monkeypatch.setattr(preparation, "apply_metadata", apply)
    monkeypatch.setattr(
        preparation,
        "base_native_versions",
        Mock(return_value={"iOS": "3.21.0", "Android": "3.16.0"}),
    )
    groups = (PRGroup(label="Crash fixes", prs=[GroupedPR(number=1, title="fix: Prevent crash")]),)

    preparation.open_preparation(
        git, source, plan, "- Fixed a crash.", token="token", groups=groups
    )

    apply.assert_called_once_with(git, plan, "- Fixed a crash.")
    source.create_reference.assert_called_once_with("heads/release/1.7.2", SOURCE)
    git.push.assert_called_once_with(
        source,
        branch="release-prep/1.7.2",
        expected_parent=SOURCE,
        create_branch=True,
        token="token",
    )
    pull = source.repository.create_pull.call_args.kwargs
    assert pull["base"] == "release/1.7.2"
    assert pull["head"] == "release-prep/1.7.2"
    assert "### Crash fixes\n\n- #1" in pull["body"]
    assert "- #2" in pull["body"]
    assert "Never rebase" in pull["body"]


def test_existing_candidate_stops_preparation_before_changes(plan, monkeypatch):
    source, git = Mock(), Mock()
    source.reference_sha.return_value = SOURCE
    apply = Mock()
    monkeypatch.setattr(preparation, "apply_metadata", apply)

    with pytest.raises(ValueError, match="Candidate .* exists"):
        preparation.open_preparation(git, source, plan, "notes", token="token")

    apply.assert_not_called()
    git.commit.assert_not_called()
    source.create_reference.assert_not_called()


@pytest.mark.parametrize(
    "mismatch", [None, "unmerged", "sha", "head", "repository", "candidate"]
)
def test_reviewed_pull_requires_one_merged_pr_for_the_exact_candidate(mismatch):
    source = Mock()
    source.repository.full_name = "DataDog/dd-sdk-unity"
    source.reference_sha.return_value = SOURCE
    repo = SimpleNamespace(full_name=source.repository.full_name)
    pull = SimpleNamespace(
        merged=True,
        merge_commit_sha=SOURCE,
        base=SimpleNamespace(ref="release/1.7.2", repo=repo),
        head=SimpleNamespace(ref="release-prep/1.7.2", repo=repo),
    )
    source.repository.get_pulls.return_value = [pull]
    if mismatch == "unmerged":
        pull.merged = False
    elif mismatch == "sha":
        pull.merge_commit_sha = "b" * 40
    elif mismatch == "head":
        pull.head.ref = "another-branch"
    elif mismatch == "repository":
        pull.head.repo = SimpleNamespace(full_name="someone/fork")
    elif mismatch == "candidate":
        source.reference_sha.return_value = "b" * 40
    if mismatch is None:
        preparation.reviewed_pull(source, "release/1.7.2", SOURCE)
    else:
        with pytest.raises(ValueError):
            preparation.reviewed_pull(source, "release/1.7.2", SOURCE)


def test_apply_metadata_updates_release_files_without_changing_sdk_code(tmp_path, plan):
    package = tmp_path / PACKAGE_PATH
    (package / "Runtime").mkdir(parents=True)
    (package / "package.json").write_text('{"version":"1.7.1"}')
    (tmp_path / ASSEMBLY_PATH).write_text(
        '[assembly: AssemblyVersion("1.7.1")]\n[assembly: AssemblyInformationalVersion("1.7.1")]\n'
    )
    code = package / "Runtime/Code.cs"
    code.write_text("SDK source")
    git = Mock(root=tmp_path)
    git.read.side_effect = lambda sha, path: {
        CHANGELOG_PATH: b"# Change Log\n\n## 1.7.1\n\n- Original.\n",
        NATIVE_HISTORY_PATH: b"| Unity SDK | iOS SDK | Android SDK |\n|---|---|---|\n| 1.7.1 | 3.20.0 | 3.15.0 |\n",
    }[path]

    preparation.apply_metadata(git, plan, "- Fixed a crash.")

    assert json.loads((package / "package.json").read_text())["version"] == "1.7.2"
    assert 'AssemblyVersion("1.7.2")' in (tmp_path / ASSEMBLY_PATH).read_text()
    assert "## 1.7.2\n\n- Fixed a crash." in (tmp_path / CHANGELOG_PATH).read_text()
    rows = SdkVersionTable.parse((tmp_path / NATIVE_HISTORY_PATH).read_text()).rows
    assert [
        (str(row.unity_version), row.ios_version_str, row.android_version_str)
        for row in rows
    ] == [
        ("1.7.2", "3.21.0", "3.16.0"),
        ("1.7.1", "3.20.0", "3.15.0"),
    ]
    assert code.read_text() == "SDK source"
