"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
from common.versions.android_deps import AndroidDependencyPin
from common.versions.semver import Version
from update_android_version import (
    format_drift_message,
    resolve_artifacts,
)


__artifacts__ = ['dd-sdk-android-rum', 'dd-sdk-android-logs', 'dd-sdk-android-ndk']


def test_resolve_artifacts_returns_pin_artifacts():
    pin = AndroidDependencyPin(version=Version.parse('3.10.0'), artifacts=list(__artifacts__))
    assert resolve_artifacts(pin) == __artifacts__


def test_resolve_artifacts_returns_a_distinct_list():
    pin = AndroidDependencyPin(version=Version.parse('3.10.0'), artifacts=list(__artifacts__))
    result = resolve_artifacts(pin)
    result.append('unexpected-artifact')
    assert pin.artifacts == __artifacts__


def test_format_drift_message_single_line():
    message = format_drift_message(['org.jetbrains.kotlin:kotlin-stdlib: expected 2.0.21, found 2.1.0'])
    assert 'org.jetbrains.kotlin:kotlin-stdlib: expected 2.0.21, found 2.1.0' in message
    assert '--allow-transitive-drift' in message


def test_format_drift_message_multiple_lines():
    message = format_drift_message([
        'org.jetbrains.kotlin:kotlin-stdlib: expected 2.0.21, found 2.1.0',
        'com.squareup.okhttp3:okhttp: expected 4.12.0, found 4.13.0',
    ])
    assert 'org.jetbrains.kotlin:kotlin-stdlib: expected 2.0.21, found 2.1.0' in message
    assert 'com.squareup.okhttp3:okhttp: expected 4.12.0, found 4.13.0' in message


def test_format_drift_message_states_pin_left_untouched():
    message = format_drift_message(['org.jetbrains.kotlin:kotlin-stdlib: expected 2.0.21, found 2.1.0'])
    assert 'the pin was left untouched' in message
