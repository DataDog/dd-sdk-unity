# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Merge-back preparation without publication or repository handoffs."""

import json
from types import SimpleNamespace
from unittest.mock import Mock

import pytest

from dd_sdk_unity.scripts.common.versions.sdkversions import SdkVersionTable

from . import ASSEMBLY_PATH, CHANGELOG_PATH, NATIVE_HISTORY_PATH, PACKAGE_PATH, integration

SOURCE = "a" * 40
DEVELOP = "b" * 40


@pytest.mark.parametrize("state,merged", [("open", False), ("closed", True), ("closed", False)])
def test_existing_integration_is_reused_or_rejected_without_cloning(monkeypatch, state, merged):
    source = Mock()
    source.repository.get_pulls.return_value = [
        SimpleNamespace(
            state=state, merged=merged, number=1, html_url="https://github.invalid/pr/1"
        )
    ]
    clone = Mock(side_effect=AssertionError("Unexpected clone"))
    monkeypatch.setattr(integration.GitOperations, "clone", clone)
    if state == "closed" and not merged:
        with pytest.raises(ValueError, match="was closed"):
            integration.integrate(source, version="1.7.2", source_sha=SOURCE, source_token="token")
    else:
        integration.integrate(source, version="1.7.2", source_sha=SOURCE, source_token="token")
    source.repository.create_pull.assert_not_called()


@pytest.mark.parametrize("dry_run", [False, True])
def test_integration_keeps_newer_development_metadata_and_reviewed_release_notes(
    tmp_path, monkeypatch, dry_run
):
    source = Mock()
    source.repository.get_pulls.return_value = []
    source.reference_sha.side_effect = lambda ref: DEVELOP if ref == "heads/develop" else None
    source.repository.clone_url = "https://github.invalid/source"
    git = Mock(root=tmp_path)
    git.repository.head.commit.hexsha = "c" * 40
    git.repository.git.diff.return_value = "integration diff"
    git.repository.is_dirty.return_value = True
    (tmp_path / PACKAGE_PATH / "Runtime").mkdir(parents=True)
    documents = {
        (SOURCE, CHANGELOG_PATH): b"# Change Log\n\n## 1.7.2\n\n- Reviewed fix.\n",
        (
            DEVELOP,
            f"{PACKAGE_PATH}/package.json",
        ): b'{"version":"2.0.0","description":"Keep development metadata"}',
        (
            DEVELOP,
            ASSEMBLY_PATH,
        ): b'[assembly: AssemblyVersion("2.0.0")]\n[assembly: AssemblyInformationalVersion("2.0.0")]\n',
        (DEVELOP, CHANGELOG_PATH): b"# Change Log\n\n## 2.0.0\n\n- New development API.\n",
        (
            DEVELOP,
            NATIVE_HISTORY_PATH,
        ): b"| Unity SDK | iOS SDK | Android SDK |\n|---|---|---|\n| 2.0.0 | 3.21.0 | 3.16.0 |\n",
    }
    git.read.side_effect = lambda sha, path: documents[sha, path]
    monkeypatch.setattr(integration.GitOperations, "clone", Mock(return_value=git))
    monkeypatch.setattr(
        integration, "pins",
        Mock(return_value=(Mock(version="3.20.0"), Mock(version="3.15.0"))),
    )

    integration.integrate(
        source, version="1.7.2", source_sha=SOURCE, source_token="token", dry_run=dry_run
    )

    package = json.loads((tmp_path / PACKAGE_PATH / "package.json").read_text())
    assert package["version"] == "2.0.0"
    assert package["description"] == "Keep development metadata"
    notes = (tmp_path / CHANGELOG_PATH).read_text()
    assert "- New development API." in notes and "- Reviewed fix." in notes
    rows = SdkVersionTable.parse((tmp_path / NATIVE_HISTORY_PATH).read_text()).rows
    assert [(str(row.unity_version), row.ios_version_str, row.android_version_str) for row in rows] == [
        ("2.0.0", "3.21.0", "3.16.0"), ("1.7.2", "3.20.0", "3.15.0"),
    ]
    if dry_run:
        git.push.assert_not_called()
        source.repository.create_pull.assert_not_called()
    else:
        git.push.assert_called_once_with(
            source,
            branch="merge-back/1.7.2",
            expected_parent=SOURCE,
            create_branch=True,
            token="token",
        )
        assert source.repository.create_pull.call_args.kwargs["base"] == "develop"
