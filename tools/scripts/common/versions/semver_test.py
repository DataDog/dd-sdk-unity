import pytest

from .semver import Version, VersionBump


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

    assert list(sorted([ver_120, ver_013])) == [ver_013, ver_120]

    assert Version.parse('5.4.3').bump(VersionBump.PATCH) == '5.4.4'
    assert Version.parse('5.4.3').bump(VersionBump.MINOR) == '5.5.0'
    assert Version.parse('5.4.3').bump(VersionBump.MAJOR) == '6.0.0'
