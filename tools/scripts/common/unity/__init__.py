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
from .asset import AssetRevertFunc, AssetPropertyValue, AssetPropertyChange, AssetModification
from .buildscript import ProjectBuildConfiguration, BuildScriptTemplate, InjectedBuildScript
from .runtimescript import RuntimeScript, InjectedRuntimeScript
from .project import UnityProject


__all__ = [
    'UnityVersion',
    'UnityLicenseStatus',
    'UnityBatchModeResult',
    'UnityInstall',
    'resolve_unity_install',
    'match_unity_version',
    'UnityHub',
    'modified_ios_target_settings',
    'AssetRevertFunc',
    'AssetPropertyValue',
    'AssetPropertyChange',
    'AssetModification',
    'ProjectBuildConfiguration',
    'BuildScriptTemplate',
    'InjectedBuildScript',
    'RuntimeScript',
    'InjectedRuntimeScript',
    'UnityProject',
]
