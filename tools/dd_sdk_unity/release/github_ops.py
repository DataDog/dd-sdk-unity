# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""GitHub reads and metadata writes through PyGithub; commit pushes live in git_ops."""

from github.GitCommit import GitCommit
from github.GithubException import GithubException
from github.GitRelease import GitRelease
from github.Repository import Repository

from dd_sdk_unity.scripts.common.log import get_default_logger


class GitHubOperations:
    def __init__(self, repository: Repository, *, read_only: bool = False) -> None:
        self.repository = repository
        self.read_only = read_only

    def _require_write(self) -> None:
        if self.read_only:
            raise RuntimeError("Remote mutation attempted through a read-only GitHub client")

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
        self._require_write()
        log.info(f"Deleting branch: {name}")
        self.repository.get_git_ref(f"heads/{name}").delete()

    def create_reference(self, ref: str, sha: str) -> None:
        self._require_write()
        self.repository.create_git_ref(f"refs/{ref}", sha)

    def create_release(self, version: str, sha: str, notes: str) -> None:
        self._require_write()
        self.repository.create_git_release(
            tag=version, name=version, message=notes, target_commitish=sha
        )
