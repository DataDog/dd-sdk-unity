# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

from dd_sdk_unity.scripts.update_android_version import format_drift_message


def test_drift_message_lists_changes_and_recovery_instructions():
    changes = [
        'org.jetbrains.kotlin:kotlin-stdlib: expected 2.0.21, found 2.1.0',
        'com.squareup.okhttp3:okhttp: expected 4.12.0, found 4.13.0',
    ]
    message = format_drift_message(changes)
    for change in changes:
        assert change in message
    assert '--allow-transitive-drift' in message
    assert 'the pin was left untouched' in message
