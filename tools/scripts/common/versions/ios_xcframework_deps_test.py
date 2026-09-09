"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import pytest

import io

from .semver import Version
from .ios_xcframework_deps import (
    IosXcframeworkPin,
    _write_ios_xcframework_pin_impl,
    read_ios_xcframework_pin,
)


__development_json__ = '''{
  "version": "3.11.1",
  "sha256": "9fe66c4b4c4e3ba68b253c701aff97447358b08b0c5b43af6d4854bf1563c13d",
  "modules": [
    "DatadogCore",
    "DatadogInternal",
    "DatadogLogs",
    "DatadogRUM",
    "DatadogCrashReporting"
  ]
}
'''


__pin__ = IosXcframeworkPin(
    version=Version.parse('3.11.1'),
    sha256='9fe66c4b4c4e3ba68b253c701aff97447358b08b0c5b43af6d4854bf1563c13d',
    modules=['DatadogCore', 'DatadogInternal', 'DatadogLogs', 'DatadogRUM', 'DatadogCrashReporting'],
)


__release_json__ = '''{
  "version": "3.12.0",
  "sha256": "aaaa66c4b4c4e3ba68b253c701aff97447358b08b0c5b43af6d4854bf1bbbb",
  "modules": [
    "DatadogCore",
    "DatadogInternal",
    "DatadogLogs",
    "DatadogRUM",
    "DatadogCrashReporting"
  ]
}
'''


__new_pin__ = IosXcframeworkPin(
    version=Version.parse('3.12.0'),
    sha256='aaaa66c4b4c4e3ba68b253c701aff97447358b08b0c5b43af6d4854bf1bbbb',
    modules=['DatadogCore', 'DatadogInternal', 'DatadogLogs', 'DatadogRUM', 'DatadogCrashReporting'],
)


def test_read_ios_xcframework_pin():
    got = read_ios_xcframework_pin(__development_json__)
    assert got == __pin__


def test_read_ios_xcframework_pin_missing_version_raises():
    bad_json = '''{
  "sha256": "9fe66c4b4c4e3ba68b253c701aff97447358b08b0c5b43af6d4854bf1563c13d",
  "modules": ["DatadogCore"]
}
'''
    with pytest.raises(RuntimeError, match='version'):
        read_ios_xcframework_pin(bad_json)


def test_read_ios_xcframework_pin_empty_modules_raises():
    bad_json = '''{
  "version": "3.11.1",
  "sha256": "9fe66c4b4c4e3ba68b253c701aff97447358b08b0c5b43af6d4854bf1563c13d",
  "modules": []
}
'''
    with pytest.raises(RuntimeError, match='modules'):
        read_ios_xcframework_pin(bad_json)


def test_write_ios_xcframework_pin_impl():
    infile = io.StringIO(__development_json__)
    outfile = io.StringIO()
    _write_ios_xcframework_pin_impl(infile, outfile, __new_pin__)
    outfile.seek(0)
    assert outfile.read() == __release_json__


def test_round_trip():
    infile = io.StringIO(__development_json__)
    outfile = io.StringIO()
    _write_ios_xcframework_pin_impl(infile, outfile, __new_pin__)
    outfile.seek(0)
    written = outfile.read()
    got = read_ios_xcframework_pin(written)
    assert got == __new_pin__


def test_write_ios_xcframework_pin_impl_null_sha256():
    infile = io.StringIO(__development_json__)
    outfile = io.StringIO()
    pin_without_sha = IosXcframeworkPin(
        version=Version.parse('3.12.0'),
        sha256=None,
        modules=['DatadogCore', 'DatadogInternal', 'DatadogLogs', 'DatadogRUM', 'DatadogCrashReporting'],
    )
    _write_ios_xcframework_pin_impl(infile, outfile, pin_without_sha)
    outfile.seek(0)
    written = outfile.read()
    assert '"sha256": null' in written
