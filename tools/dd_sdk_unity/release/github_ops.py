# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""GitHub reads and metadata writes through PyGithub; commit pushes live in git_ops."""

from pathlib import Path

from github.GitCommit import GitCommit
from github.GithubException import GithubException
from github.GitRelease import GitRelease
from github.PullRequest import PullRequest
from github.Repository import Repository


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

    def merged_pulls(self, sha: str) -> list[PullRequest]:
        return [pull for pull in self.repository.get_commit(sha).get_pulls() if pull.merged]

    def release(self, version: str) -> GitRelease | None:
        try:
            return self.repository.get_release(version)
        except GithubException as error:
            if error.status == 404:
                return None
            raise

    def download_asset(self, release: GitRelease, name: str, output: Path) -> None:
        assets = [asset for asset in release.get_assets() if asset.name == name]
        if len(assets) != 1:
            raise ValueError(f"Expected one {name} asset on release {release.tag_name}")
        assets[0].download_asset(str(output), chunk_size=1024 * 1024)

    def delete_branch(self, name: str, expected_sha: str) -> None:
        current = self.reference_sha(f"heads/{name}")
        if current is None:
            return
        if current != expected_sha:
            raise ValueError(f"Refusing to delete changed branch {name}")
        self._require_write()
        print(f"Deleting branch: {name}")
        self.repository.get_git_ref(f"heads/{name}").delete()

    def ensure_reference(self, ref: str, sha: str) -> None:
        existing = self.reference_sha(ref)
        if existing is not None:
            if existing != sha:
                raise ValueError(f"Conflicting {ref}: expected {sha}, found {existing}")
            return
        self._require_write()
        self.repository.create_git_ref(f"refs/{ref}", sha)

    def ensure_release(self, version: str, sha: str, notes: str) -> GitRelease:
        existing_sha = self.reference_sha(f"tags/{version}")
        if existing_sha is not None and existing_sha != sha:
            raise ValueError(f"Conflicting package tag {version}: {existing_sha}")
        release = self.release(version)
        if release is not None:
            if existing_sha != sha:
                raise ValueError(f"Release {version} does not match the intended publication")
            if release.draft or release.prerelease:
                raise ValueError(f"Release {version} is not a completed public release")
            return release
        self._require_write()
        return self.repository.create_git_release(
            tag=version, name=version, message=notes, target_commitish=sha
        )
