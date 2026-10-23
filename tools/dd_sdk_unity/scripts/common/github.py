# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""GitHub metadata reads and Depot-aware release asset URLs."""

import os
from collections.abc import Generator
from contextlib import contextmanager

from github import Auth, Github
from github.GithubException import GithubException
from github.GitRelease import GitRelease
from github.Repository import Repository
from pydantic import BaseModel

from dd_sdk_unity.scripts.common.versions import Version


class GitHubRelease(BaseModel):
    name: str
    url: str
    html_url: str
    body: str


def get_release_asset_url(owner: str, repo: str, tag: str, asset: str) -> str:
    base = os.environ.get("DEPOT_GITHUB_RELEASES_BASE", "https://github.com").rstrip("/")
    return f"{base}/{owner}/{repo}/releases/download/{tag}/{asset}"


@contextmanager
def _repository(owner: str, repo: str) -> Generator[Repository]:
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    client = Github(auth=Auth.Token(token) if token else None, per_page=100, timeout=30)
    try:
        yield client.get_repo(f"{owner}/{repo}")
    except GithubException as error:
        # Preserve the interactive release tool's compatibility/fallback paths.
        raise RuntimeError(
            f"GitHub read failed for {owner}/{repo} (HTTP {error.status})"
        ) from error
    finally:
        client.close()


def _release(release: GitRelease) -> GitHubRelease:
    return GitHubRelease(
        name=release.tag_name,
        url=release.url,
        html_url=release.html_url,
        body=release.body or "",
    )


def resolve_latest_release_version(owner: str, repo: str) -> Version:
    with _repository(owner, repo) as repository:
        return Version.parse(repository.get_latest_release().tag_name)


def get_releases_between(
    owner: str,
    repo: str,
    start_version_exclusive: Version,
    end_version_inclusive: Version,
) -> list[GitHubRelease]:
    with _repository(owner, repo) as repository:
        releases = []
        for release in repository.get_releases():
            if release.draft or release.prerelease:
                continue
            try:
                version = Version.parse(release.tag_name)
            except ValueError:
                continue
            releases.append((version, _release(release)))
    for required in (start_version_exclusive, end_version_inclusive):
        if not any(version == required for version, _ in releases):
            raise RuntimeError(f"Releases for {owner}/{repo} do not include version {required}")
    return [
        release
        for version, release in sorted(releases, key=lambda item: item[0], reverse=True)
        if start_version_exclusive < version and not end_version_inclusive < version
    ]


def get_file_contents(owner: str, repo: str, ref: str, path: str) -> str:
    with _repository(owner, repo) as repository:
        contents = repository.get_contents(path, ref=ref)
        if isinstance(contents, list):
            raise TypeError(f"Expected a file at {path}, found a directory")
        return contents.decoded_content.decode("utf-8")
