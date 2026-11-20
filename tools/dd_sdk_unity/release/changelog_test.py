# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Changelog drafting and native-note filtering."""

import json
from unittest.mock import Mock

import pytest

from . import NATIVE_HISTORY_PATH
from .changelog import draft_notes
from .changelog_prompts import ChangelogEntry, ChangelogEntryList, GroupedPR, GroupedPRs, PRGroup

BASE_HISTORY = b"""| Unity SDK | iOS SDK | Android SDK | C++ SDK |
|---|---|---|---|
| 1.0.0 | 1.0.0 | 1.0.0 | 0.9.0 |
"""


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
    notes, groups = draft_notes(
        (classify(prefix + "chore: Internal cleanup", number=1),), "", prompt
    )
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


def test_gateway_prompt_uses_structured_responses(monkeypatch):
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
        assert sha == "source"
        assert path == NATIVE_HISTORY_PATH
        return BASE_HISTORY

    git.read.side_effect = read
    plan = Mock()
    plan.release.source_sha, plan.release.base_version = "source", "1.0.0"
    plan.ios.version, plan.android.version = ios, android
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
    git.read.return_value = BASE_HISTORY
    plan = Mock(release=Mock(source_sha="source", base_version="1.0.0"))
    plan.ios.version, plan.android.version = "1.1.0", "1.0.0"
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


def test_native_notes_skip_a_missing_base_version(monkeypatch):
    from . import changelog

    git = Mock()
    git.read.return_value = BASE_HISTORY
    plan = Mock(release=Mock(source_sha="source", base_version="1.7.1"))
    plan.ios.version, plan.android.version = "2.30.1", "2.26.3"
    upstream = Mock()
    monkeypatch.setattr(changelog.GitHubOperations, "get_file_contents", upstream)
    prompt = Mock()

    assert changelog.native_release_notes(git, plan, prompt) == ""
    git.read.assert_called_once_with("source", NATIVE_HISTORY_PATH)
    upstream.assert_not_called()
    prompt.assert_not_called()
