import pytest

import re
from dataclasses import fields

from .github import get_latest_external_dependency_versions


def test_get_latest_external_dependency_versions():
    got = get_latest_external_dependency_versions()
    assert _is_valid_semver(got.dd_sdk_android)
    assert _is_valid_semver(got.dd_sdk_ios)
    assert len(fields(got)) == 2, "new external dependency added; please update this test"


def _is_valid_semver(s: str) -> bool:
    pattern = re.compile(r'^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$')
    return pattern.match(s) is not None
