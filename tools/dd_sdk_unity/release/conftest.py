# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Release tests mock GitPython and keep package file operations real."""

from unittest.mock import MagicMock, Mock

import pytest
from git import Git, Repo
from git.objects import Blob, Commit, Tree

from . import PACKAGE_PATH


@pytest.fixture(autouse=True)
def no_git_commands(monkeypatch):
    monkeypatch.setattr(Git, "execute", Mock(side_effect=AssertionError("Git must be mocked")))


@pytest.fixture
def repository(tmp_path):
    repo = MagicMock(spec=Repo)
    repo.working_tree_dir = str(tmp_path)
    repo.is_dirty.return_value = False
    return repo


@pytest.fixture
def make_commit():
    def make(sha, files=None, *, message="", parents=()):
        blobs = {}
        for path, data in (files or {}).items():
            blob = Mock(spec=Blob)
            blob.path, blob.mode = path, 0o100644
            blob.data_stream.read.return_value = data.encode() if isinstance(data, str) else data
            blobs[path] = blob
        package = MagicMock(spec=Tree)
        package.traverse.return_value = [
            blob for path, blob in blobs.items() if path.startswith(f"{PACKAGE_PATH}/")
        ]
        tree = MagicMock(spec=Tree)
        tree.hexsha = sha
        tree.__truediv__.side_effect = lambda path: (
            package if path == PACKAGE_PATH else blobs[path]
        )
        commit = Mock(spec=Commit)
        commit.hexsha, commit.message, commit.parents, commit.tree = sha, message, parents, tree
        commit.committed_date = 1794484800  # 2026-11-12 12:00:00 UTC
        return commit

    return make
