import requests
from typing import Dict, Any

from common.types import ExternalDependencyVersions


def get_latest_external_dependency_versions() -> ExternalDependencyVersions:
    return ExternalDependencyVersions(
        dd_sdk_android=_resolve_latest_release_name('DataDog', 'dd-sdk-android'),
        dd_sdk_ios=_resolve_latest_release_name('DataDog', 'dd-sdk-ios'),
    )


def _resolve_latest_release_name(owner: str, repo: str) -> str:
    release = _fetch_github_release(owner, repo, 'latest')
    release_name = release.get('name')
    if not release_name:
        raise RuntimeError('Release JSON from GitHub API does not include a valid name')
    return release_name


def _fetch_github_release(owner: str, repo: str, release: str) -> Dict[str, Any]:
    url = f'https://api.github.com/repos/{owner}/{repo}/releases/{release}'
    res = requests.get(url)
    if not res.ok:
        raise RuntimeError(f'GET {url} failed with status code {res.status_code}')
    data = res.json()
    if not isinstance(data, dict):
        raise TypeError(f'Response body from GET {url} could not be parsed as JSON')
    return data
