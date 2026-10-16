# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for fetching publicly-accessible information about GitHub repositories via
public endpoints in the GitHub API.
"""

import requests
from pydantic import BaseModel

from dd_sdk_unity.scripts.common.versions import Version


class GitHubRelease(BaseModel):
    name: str
    url: str
    html_url: str
    body: str


def resolve_latest_release_version(owner: str, repo: str) -> Version:
    release = _fetch_github_release(owner, repo, 'latest')
    return Version.parse(release.name)


def get_releases_between(owner: str, repo: str, start_version_exclusive: Version, end_version_inclusive: Version) -> list[GitHubRelease]:
    # For simplicity, fetch the details of all known releases, ordered by creation date (newest first)
    releases = _fetch_github_releases(owner, repo)

    # Find the version at the end of our range, and omit any releases that are newer
    newest_index = next((i for i, x in enumerate(releases) if x.name == end_version_inclusive), None)
    if newest_index is None:
        raise RuntimeError(f'List of releases fetched from repo {owner}/{repo} does not include version {end_version_inclusive}')
    releases = releases[newest_index:]

    # Find the version that we want to start from, and omit it and releases that are older
    oldest_index = next((i for i, x in enumerate(releases) if x.name == start_version_exclusive), None)
    if oldest_index is None:
        raise RuntimeError(f'List of releases fetched from repo {owner}/{repo} does not include version {start_version_exclusive}')
    return releases[0:oldest_index]


def get_file_contents(owner: str, repo: str, ref: str, path: str) -> str:
    url = f'https://raw.githubusercontent.com/{owner}/{repo}/{ref}/{path}'
    res = requests.get(url)
    if not res.ok:
        raise RuntimeError(f'GET {url} failed with status code {res.status_code}')
    return res.text


def _fetch_github_releases(owner: str, repo: str) -> list[GitHubRelease]:
    releases: list[GitHubRelease] = []
    per_page = 100
    max_pages = 50
    page = 1
    for i in range(max_pages):
        url = f'https://api.github.com/repos/{owner}/{repo}/releases?per_page={per_page}&page={page}'
        res = requests.get(url)
        if not res.ok:
            raise RuntimeError(f'GET {url} failed with status code {res.status_code}')
        data = res.json()
        if not isinstance(data, list):
            raise ValueError(f'GET {url} did not return a JSON array')  # noqa: TRY004 -- Invalid API data, not a caller argument.
        if not data:
            break
        for obj in data:
            releases.append(GitHubRelease(**obj))
        page += 1
    return releases


def _fetch_github_release(owner: str, repo: str, release: str) -> GitHubRelease:
    url = f'https://api.github.com/repos/{owner}/{repo}/releases/{release}'
    res = requests.get(url)
    if not res.ok:
        raise RuntimeError(f'GET {url} failed with status code {res.status_code}')
    data = res.json()
    return GitHubRelease(**data)
