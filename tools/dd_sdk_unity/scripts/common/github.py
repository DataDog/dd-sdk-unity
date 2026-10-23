# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""Repository file reads and release asset URLs."""

import os

import requests
from github import Auth, Github


def get_release_asset_url(owner: str, repo: str, tag: str, asset: str) -> str:
    base = os.environ.get("DEPOT_GITHUB_RELEASES_BASE", "https://github.com").rstrip("/")
    return f"{base}/{owner}/{repo}/releases/download/{tag}/{asset}"


def get_file_contents(owner: str, repo: str, ref: str, path: str) -> str:
    if token := os.environ.get("CI_JOB_TOKEN"):
        response = requests.get(
            "https://gitretriever.us1.ddbuild.io/api/file",
            params={"repo": f"{owner}/{repo}", "ref": ref, "path": path},
            headers={"Authorization": f"Bearer {token}"},
            timeout=30,
        )
        response.raise_for_status()
        return response.content.decode("utf-8")
    token = os.environ.get("GITHUB_TOKEN")
    with Github(auth=Auth.Token(token) if token else None, per_page=100, timeout=30) as client:
        contents = client.get_repo(f"{owner}/{repo}").get_contents(path, ref=ref)
        if isinstance(contents, list):
            raise TypeError(f"Expected a file at {path}, found a directory")
        return contents.decoded_content.decode("utf-8")
