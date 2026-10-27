# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

from unittest.mock import Mock

import pytest
import requests
from github.GithubException import GithubException

from dd_sdk_unity.scripts.common import github


@pytest.fixture
def repository(monkeypatch):
    monkeypatch.delenv("CI_JOB_TOKEN", raising=False)
    repository = Mock(full_name="DataDog/dd-sdk-ios")
    client = Mock()
    client.get_repo.return_value = repository
    monkeypatch.setattr(github, "Github", Mock(return_value=client))
    return repository


def test_local_file_reads_use_pygithub_contents_api(repository):
    operations = github.GitHubOperations(repository.full_name)
    repository.get_contents.return_value.decoded_content = b'{"version":"3.0.0"}'
    assert (
        operations.get_file_contents("3.0.0", "package.json") == '{"version":"3.0.0"}'
    )
    repository.get_contents.assert_called_once_with("package.json", ref="3.0.0")
    repository.get_contents.side_effect = GithubException(404, {"message": "Not found"})
    with pytest.raises(GithubException):
        operations.get_file_contents("3.0.0", "old-path.json")


@pytest.mark.parametrize("status", [200, 403])
def test_ci_file_reads_use_gitretriever_without_github_fallback(monkeypatch, repository, status):
    operations = github.GitHubOperations(repository.full_name)
    monkeypatch.setenv("CI_JOB_TOKEN", "job-token")
    response = requests.Response()
    response.status_code = status
    response._content = "# 3.0.0\nFixes UTF-8 decoding: →\n".encode()
    get = Mock(return_value=response)
    monkeypatch.setattr(github.requests, "get", get)

    if status == 200:
        assert operations.get_file_contents("refs/tags/3.0.0", "CHANGELOG.md") == (
            response.content.decode("utf-8")
        )
    else:
        with pytest.raises(requests.HTTPError):
            operations.get_file_contents("refs/tags/3.0.0", "CHANGELOG.md")
    get.assert_called_once_with(
        "https://gitretriever.us1.ddbuild.io/api/file",
        params={"repo": "DataDog/dd-sdk-ios", "ref": "refs/tags/3.0.0", "path": "CHANGELOG.md"},
        headers={"Authorization": "Bearer job-token"},
        timeout=30,
    )
    repository.get_contents.assert_not_called()


@pytest.mark.parametrize("base", [None, "https://depot.example.invalid/github-releases/"])
def test_release_asset_url_uses_configured_depot_or_github(monkeypatch, repository, base):
    operations = github.GitHubOperations(repository.full_name)
    if base is None:
        monkeypatch.delenv("DEPOT_GITHUB_RELEASES_BASE", raising=False)
    else:
        monkeypatch.setenv("DEPOT_GITHUB_RELEASES_BASE", base)
    expected_base = base.rstrip("/") if base else "https://github.com"
    assert operations.get_release_asset_url("3.11.1", "sdk.zip") == (
        f"{expected_base}/DataDog/dd-sdk-ios/releases/download/3.11.1/sdk.zip"
    )


@pytest.mark.parametrize("token", [None, "explicit-token"])
def test_client_owns_auth_lazy_setup_and_cleanup(monkeypatch, token):
    monkeypatch.setenv("GITHUB_TOKEN", "default-token")
    client = Mock()
    factory = Mock(return_value=client)
    monkeypatch.setattr(github, "Github", factory)
    with (
        pytest.raises(RuntimeError, match="operation failed"),
        github.GitHubOperations("example/sdk", token=token) as operations,
    ):
        assert operations.token == (token or "default-token")
        assert factory.call_args.kwargs["auth"].token == operations.token
        assert factory.call_args.kwargs["lazy"] is True
        raise RuntimeError("operation failed")
    client.get_repo.assert_called_once_with("example/sdk")
    client.close.assert_called_once_with()
