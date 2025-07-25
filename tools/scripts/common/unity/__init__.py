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
from .archive import UnityArchiveRelease, find_archive_release
from .ios_settings import modified_ios_target_settings
from .injected_script import ProjectBuildConfiguration, InjectedScript, InjectedScriptContext
from .project import UnityProject
from .build import UnityBuild, UnityBuildPlatform, UnityBuildConfig, UnityTarget, DatadogBackendType


__all__ = [
    'UnityVersion',
    'UnityLicenseStatus',
    'UnityBatchModeResult',
    'UnityInstall',
    'resolve_unity_install',
    'match_unity_version',
    'UnityHub',
    'UnityArchiveRelease',
    'find_archive_release',
    'modified_ios_target_settings',
    'ProjectBuildConfiguration',
    'InjectedScript',
    'InjectedScriptContext',
    'UnityProject',
    'UnityBuild',
    'UnityBuildPlatform',
    'UnityBuildConfig',
    'UnityTarget',
    'DatadogBackendType',
]
