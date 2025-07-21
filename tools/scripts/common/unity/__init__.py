"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
from .install import (
    UnityVersion,
    UnityLicenseStatus,
    UnityBatchModeResult,
    UnityInstall,
    resolve_unity_install,
    match_unity_version,
)
from .hub import UnityHub
from .ios_settings import modified_ios_target_settings


__all__ = [
    'UnityVersion',
    'UnityLicenseStatus',
    'UnityBatchModeResult',
    'UnityInstall',
    'resolve_unity_install',
    'match_unity_version',
    'UnityHub',
    'modified_ios_target_settings',
]
