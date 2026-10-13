"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
from dd_sdk_unity.scripts.common.unity.install import (
    UnityVersion,
    UnityInstall,
    resolve_unity_install,
    match_unity_version,
)
from dd_sdk_unity.scripts.common.unity.cli import UnityCli
from dd_sdk_unity.scripts.common.unity.ios_settings import modified_ios_target_settings


__all__ = [
    'UnityVersion',
    'UnityInstall',
    'resolve_unity_install',
    'match_unity_version',
    'UnityCli',
    'modified_ios_target_settings',
]
