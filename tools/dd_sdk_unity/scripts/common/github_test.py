# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

from types import SimpleNamespace
from unittest.mock import Mock

import pytest
from github.GithubException import GithubException

from dd_sdk_unity.scripts.common import github
from dd_sdk_unity.scripts.common.versions import Version


@pytest.fixture
def repository(monkeypatch):
    repo = Mock()
    client = Mock()
    client.get_repo.return_value = repo
    monkeypatch.setattr(github, "Github", Mock(return_value=client))
    return repo


def release(version, *, draft=False, prerelease=False):
    return SimpleNamespace(
        tag_name=version,
        name="Display title is not a version",
        url=f"api/{version}",
        html_url=f"releases/{version}",
        body=None,
        draft=draft,
        prerelease=prerelease,
    )


def test_native_release_range_is_semantic_not_creation_order(repository):
    repository.get_releases.return_value = [
        release("3.9.1"),
        release("3.10.0"),
        release("3.9.0"),
        release("3.11.0-rc1", prerelease=True),
    ]
    result = github.get_releases_between(
        "example", "sdk", Version.parse("3.9.0"), Version.parse("3.10.0")
    )
    assert [r.name for r in result] == ["3.10.0", "3.9.1"]
    assert result[0].body == ""


def test_missing_range_endpoint_is_not_silently_ignored(repository):
    repository.get_releases.return_value = [release("3.10.0")]
    with pytest.raises(RuntimeError, match="3.9.0"):
        github.get_releases_between(
            "example", "sdk", Version.parse("3.9.0"), Version.parse("3.10.0")
        )


def test_file_reads_use_pygithub_contents_api_and_preserve_failure_contract(repository):
    repository.get_contents.return_value.decoded_content = b'{"version":"3.0.0"}'
    assert (
        github.get_file_contents("example", "sdk", "3.0.0", "package.json") == '{"version":"3.0.0"}'
    )
    repository.get_contents.assert_called_once_with("package.json", ref="3.0.0")
    repository.get_contents.side_effect = GithubException(404, {"message": "Not found"})
    with pytest.raises(RuntimeError, match="HTTP 404"):
        github.get_file_contents("example", "sdk", "3.0.0", "old-path.json")


@pytest.mark.parametrize("base", [None, "https://depot.example.invalid/github-releases/"])
def test_release_asset_url_uses_configured_depot_or_github(monkeypatch, base):
    if base is None:
        monkeypatch.delenv("DEPOT_GITHUB_RELEASES_BASE", raising=False)
    else:
        monkeypatch.setenv("DEPOT_GITHUB_RELEASES_BASE", base)
    expected_base = base.rstrip("/") if base else "https://github.com"
    assert github.get_release_asset_url("DataDog", "dd-sdk-ios", "3.11.1", "sdk.zip") == (
        f"{expected_base}/DataDog/dd-sdk-ios/releases/download/3.11.1/sdk.zip"
    )
