"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import pytest

import io

from .semver import Version
from .android_deps import (
    AndroidDependencyPin,
    _write_android_dependency_pin_impl,
    read_android_dependency_pin,
)


__development_json__ = '''{
  "version": "3.10.0",
  "artifacts": [
    "dd-sdk-android-rum",
    "dd-sdk-android-logs",
    "dd-sdk-android-ndk"
  ]
}
'''


__pin__ = AndroidDependencyPin(
    version=Version.parse('3.10.0'),
    artifacts=['dd-sdk-android-rum', 'dd-sdk-android-logs', 'dd-sdk-android-ndk'],
)


__release_json__ = '''{
  "version": "3.12.0",
  "artifacts": [
    "dd-sdk-android-rum",
    "dd-sdk-android-logs",
    "dd-sdk-android-ndk"
  ]
}
'''


__new_pin__ = AndroidDependencyPin(
    version=Version.parse('3.12.0'),
    artifacts=['dd-sdk-android-rum', 'dd-sdk-android-logs', 'dd-sdk-android-ndk'],
)


def test_read_android_dependency_pin():
    got = read_android_dependency_pin(__development_json__)
    assert got == __pin__


def test_read_android_dependency_pin_missing_version_raises():
    bad_json = '''{
  "artifacts": ["dd-sdk-android-rum"]
}
'''
    with pytest.raises(RuntimeError, match='version'):
        read_android_dependency_pin(bad_json)


def test_read_android_dependency_pin_missing_artifacts_raises():
    bad_json = '''{
  "version": "3.10.0"
}
'''
    with pytest.raises(RuntimeError, match='artifacts'):
        read_android_dependency_pin(bad_json)


def test_read_android_dependency_pin_empty_artifacts_raises():
    bad_json = '''{
  "version": "3.10.0",
  "artifacts": []
}
'''
    with pytest.raises(RuntimeError, match='artifacts'):
        read_android_dependency_pin(bad_json)


def test_write_android_dependency_pin_impl():
    infile = io.StringIO(__development_json__)
    outfile = io.StringIO()
    _write_android_dependency_pin_impl(infile, outfile, __new_pin__)
    outfile.seek(0)
    assert outfile.read() == __release_json__


def test_round_trip():
    infile = io.StringIO(__development_json__)
    outfile = io.StringIO()
    _write_android_dependency_pin_impl(infile, outfile, __new_pin__)
    outfile.seek(0)
    written = outfile.read()
    got = read_android_dependency_pin(written)
    assert got == __new_pin__


def test_write_android_dependency_pin_impl_no_sha256():
    infile = io.StringIO(__development_json__)
    outfile = io.StringIO()
    _write_android_dependency_pin_impl(infile, outfile, __new_pin__)
    outfile.seek(0)
    written = outfile.read()
    assert 'sha256' not in written
