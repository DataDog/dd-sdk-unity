# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""GitHub repository reads and metadata writes."""

import os
from typing import Self

import requests
from github import Auth, Github
from github.GitCommit import GitCommit
from github.GithubException import GithubException
from github.GitRelease import GitRelease
from github.Repository import Repository

from dd_sdk_unity.scripts.common.log import get_default_logger


class GitHubOperations:
    def __init__(self, repository: str, *, token: str | None = None) -> None:
        self.token = token or os.getenv("GITHUB_TOKEN")
        self._client = Github(
            auth=Auth.Token(self.token) if self.token else None,
            lazy=True,
            per_page=100,
            timeout=30,
        )
        self.repository: Repository = self._client.get_repo(repository)

    def __enter__(self) -> Self:
        return self

    def __exit__(self, exc_type, exc_value, traceback) -> None:
        self._client.close()

    def get_release_asset_url(self, tag: str, asset: str) -> str:
        base = os.environ.get("DEPOT_GITHUB_RELEASES_BASE", "https://github.com").rstrip("/")
        return f"{base}/{self.repository.full_name}/releases/download/{tag}/{asset}"

    def get_file_contents(self, ref: str, path: str) -> str:
        if token := os.environ.get("CI_JOB_TOKEN"):
            response = requests.get(
                "https://gitretriever.us1.ddbuild.io/api/file",
                params={"repo": self.repository.full_name, "ref": ref, "path": path},
                headers={"Authorization": f"Bearer {token}"},
                timeout=30,
            )
            response.raise_for_status()
            return response.content.decode("utf-8")
        contents = self.repository.get_contents(path, ref=ref)
        if isinstance(contents, list):
            raise TypeError(f"Expected a file at {path}, found a directory")
        return contents.decoded_content.decode("utf-8")

    def reference_sha(self, ref: str) -> str | None:
        """Resolve a heads/... or tags/... ref, peeling annotated tags."""
        try:
            obj = self.repository.get_git_ref(ref).object
        except GithubException as error:
            if error.status == 404:
                return None
            raise
        while obj.type == "tag":
            obj = self.repository.get_git_tag(obj.sha).object
        if obj.type != "commit":
            raise ValueError(f"Expected a commit at {ref}, got {obj.type}")
        return obj.sha

    def git_commit(self, sha: str) -> GitCommit:
        return self.repository.get_git_commit(sha)

    def release(self, version: str) -> GitRelease | None:
        try:
            return self.repository.get_release(version)
        except GithubException as error:
            if error.status == 404:
                return None
            raise

    def delete_branch(self, name: str, expected_sha: str) -> None:
        log = get_default_logger()
        current = self.reference_sha(f"heads/{name}")
        if current is None:
            return
        if current != expected_sha:
            raise ValueError(f"Refusing to delete changed branch {name}")
        log.info(f"Deleting branch: {name}")
        self.repository.get_git_ref(f"heads/{name}").delete()

    def create_reference(self, ref: str, sha: str) -> None:
        self.repository.create_git_ref(f"refs/{ref}", sha)

    def create_release(self, version: str, sha: str, notes: str) -> None:
        self.repository.create_git_release(
            tag=version, name=version, message=notes, target_commitish=sha
        )
