# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Local Git through GitPython, with commit-headless encapsulated for signed pushes."""

import os
import re
import subprocess
from pathlib import Path

from git import Actor, Repo

from .github_ops import GitHubOperations


class GitOperations:
    def __init__(self, repository: Repo) -> None:
        self.repository = repository

    @property
    def root(self) -> Path:
        if self.repository.working_tree_dir is None:
            raise ValueError("Release operations require a working tree")
        return Path(self.repository.working_tree_dir)

    def read(self, revision: str, path: str) -> bytes:
        return (self.repository.commit(revision).tree / path).data_stream.read()

    def commit(self, message: str) -> str:
        repo = self.repository
        repo.git.add("--all")
        actor = Actor("dd-octo-sts[bot]", "200755185+dd-octo-sts[bot]@users.noreply.github.com")
        # Local commits are unsigned inputs; commit-headless signs the remote result.
        with repo.git.custom_environment(
            GIT_CONFIG_COUNT="1", GIT_CONFIG_KEY_0="commit.gpgsign", GIT_CONFIG_VALUE_0="false"
        ):
            return repo.index.commit(message, author=actor, committer=actor).hexsha

    def push(
        self,
        github: GitHubOperations,
        *,
        branch: str,
        expected_parent: str,
        create_branch: bool = False,
        token: str | None = None,
    ) -> str:
        repo = self.repository
        if repo.is_dirty(untracked_files=True):
            raise ValueError("Commit local changes before requesting a signed push")
        if not re.fullmatch(r"[0-9a-f]{40}", expected_parent):
            raise ValueError("The expected parent must be a full commit SHA")
        local = repo.head.commit
        if len(local.parents) != 1 or local.parents[0].hexsha != expected_parent:
            raise ValueError(
                "A signed push requires one single-parent commit on the expected parent"
            )
        remote_parent = github.reference_sha(f"heads/{branch}")
        if create_branch:
            if remote_parent is not None:
                raise ValueError(f"Remote branch {branch} already exists")
            # New refs must still root at an object that is already on GitHub.
            github.git_commit(expected_parent)
        elif remote_parent != expected_parent:
            raise ValueError(f"Remote branch {branch} moved: {remote_parent}")
        if github.read_only:
            raise RuntimeError("A live push requires a writable GitHub client")
        if not token:
            raise ValueError("A signing token is required for a live push")
        command = [
            "commit-headless",
            "push",
            "-T",
            github.repository.full_name,
            "--branch",
            branch,
            "--head-sha",
            expected_parent,
        ]
        if create_branch:
            command.append("--create-branch")
        environment = {**os.environ, "HEADLESS_TOKEN": token}
        try:
            pushed = subprocess.run(
                command,
                cwd=repo.working_tree_dir,
                env=environment,
                capture_output=True,
                text=True,
                check=True,
                timeout=1800,
            )
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
            # Do not automatically replay an unknown remote outcome.
            stderr = error.stderr or ""
            if isinstance(stderr, bytes):
                stderr = stderr.decode(errors="replace")
            raise RuntimeError(
                f"Signed push failed; inspect remote {branch} before retrying: "
                f"{stderr.replace(token, '<redacted>')}"
            ) from None
        signed_sha = pushed.stdout.strip()
        if not re.fullmatch(r"[0-9a-f]{40}", signed_sha) or signed_sha == "0" * 40:
            raise RuntimeError("Signing returned no valid remote SHA; inspect remote state")
        if github.reference_sha(f"heads/{branch}") != signed_sha:
            raise RuntimeError("Remote branch does not match the signing result")
        signed = github.git_commit(signed_sha)
        if (
            signed.tree.sha != local.tree.hexsha
            or len(signed.parents) != 1
            or signed.parents[0].sha != expected_parent
        ):
            raise RuntimeError("Signed commit changed the intended tree or parent")
        return signed_sha

    @classmethod
    def clone(
        cls,
        url: str,
        path: Path,
        *,
        revision: str,
        branch: str = "release-source",
    ):
        repository = Repo.clone_from(url, path, no_checkout=True)
        with repository.config_writer() as config:
            config.set_value("core", "fsmonitor", False)
            config.set_value("core", "filemode", True)
        base = repository.create_head("release-base", repository.commit(revision).hexsha)
        base.checkout()
        if branch in repository.heads:
            repository.heads[branch].set_commit(base.commit)
            repository.heads[branch].checkout()
        else:
            repository.create_head(branch, base).checkout()
        return cls(repository)
