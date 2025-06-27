import pytest

from dataclasses import fields

from .github import get_latest_external_dependency_versions


def test_get_latest_external_dependency_versions():
    got = get_latest_external_dependency_versions()
    assert len(fields(got)) == 2, "new external dependency added; please update this test"
