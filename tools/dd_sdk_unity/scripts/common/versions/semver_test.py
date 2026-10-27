# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

from itertools import pairwise

import pytest

from dd_sdk_unity.scripts.common.versions.semver import Version, VersionBump


def test_VersionBump():
    assert VersionBump.PATCH < VersionBump.MINOR
    assert VersionBump.MINOR < VersionBump.MAJOR
    assert max(VersionBump.PATCH, VersionBump.MAJOR) == VersionBump.MAJOR


def test_Version():
    ver_120 = Version.parse('1.2.0')
    assert ver_120 == Version(
        major=1,
        minor=2,
        patch=0,
    )
    ver_013 = Version.parse('0.1.3')
    assert ver_013 == Version(
        major=0,
        minor=1,
        patch=3,
    )
    assert ver_013 < ver_120
    assert ver_013 != ver_120
    assert ver_120 == '1.2.0'

    assert sorted([ver_120, ver_013]) == [ver_013, ver_120]

    assert Version.parse('5.4.3').bump(VersionBump.PATCH) == '5.4.4'
    assert Version.parse('5.4.3').bump(VersionBump.MINOR) == '5.5.0'
    assert Version.parse('5.4.3').bump(VersionBump.MAJOR) == '6.0.0'


@pytest.mark.parametrize("value", [
    "0.0.0", "2.0.0-preview1", "2.0.0-preview.1+build.01", "1.0.0-0.3.7",
    "1.0.0-x-y-z.--", "1.0.0-01alpha", "1.0.0+001",
])
def test_semver_round_trip(value):
    assert str(Version.parse(value)) == value


@pytest.mark.parametrize("value", [
    "2.0.0.preview1", "v2.0.0", "2.0", "02.0.0", "2.00.0", "2.0.00",
    "2.0.0-01", "2.0.0-preview.01", "2.0.0-", "2.0.0+", "2.0.0-preview..1",
    "2.0.0+build..1", "2.0.0-preview_1", "2.0.0-préview1", "2.0.0\n",
])
def test_invalid_semver_is_rejected(value):
    with pytest.raises(ValueError, match="SemVer"):
        Version.parse(value)


def test_semver_precedence():
    ordered = [
        "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
        "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "2.0.0-preview1",
        "2.0.0", "2.0.1", "2.1.0", "2.10.0", "10.0.0",
    ]
    versions = [Version.parse(value) for value in ordered]
    assert sorted(versions[::2] + versions[1::2]) == versions
    for before, after in pairwise(versions):
        assert before < after
        assert before <= after
        assert after > before
        assert after >= before
        assert before != after


def test_build_metadata_does_not_change_precedence_or_version_bumps():
    first = Version.parse("2.0.0-preview.1+build.1")
    second = Version.parse("2.0.0-preview.1+build.2")
    assert first == second
    assert first <= second and first >= second
    assert not first < second and not second < first
    assert str(first) != str(second)
    assert first.core == "2.0.0"
    assert first.bump(VersionBump.PATCH) == "2.0.1"
