import pytest

import os

from .paths import repo_path


def test_repo_path():
    got = repo_path('packages', 'Datadog.Unity', 'Runtime', 'AssemblyInfo.cs')
    assert os.path.isfile(got)
