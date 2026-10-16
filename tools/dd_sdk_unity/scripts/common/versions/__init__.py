# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for reading and writing the various file formats in the dd-sdk-unity
repository that encode version information.
"""
from dd_sdk_unity.scripts.common.versions.android_deps import (
    ANDROID_DEPENDENCY_VERSION_RELPATH,
    AndroidDependencyPin,
    read_android_dependency_pin,
    write_android_dependency_pin,
)
from dd_sdk_unity.scripts.common.versions.assemblyinfo import modify_assemblyinfo
from dd_sdk_unity.scripts.common.versions.edm4u_deps import (
    ExternalDependencyVersions,
    read_external_dependency_versions,
)
from dd_sdk_unity.scripts.common.versions.ios_xcframework_deps import (
    IOS_DEPENDENCY_VERSION_RELPATH,
    IosXcframeworkPin,
    read_ios_xcframework_pin,
    write_ios_xcframework_pin,
)
from dd_sdk_unity.scripts.common.versions.package import modify_package_json
from dd_sdk_unity.scripts.common.versions.sdkversions import SdkVersionTable, SdkVersionTableRow
from dd_sdk_unity.scripts.common.versions.semver import Version, VersionBump

__all__ = [
    'ANDROID_DEPENDENCY_VERSION_RELPATH',
    'IOS_DEPENDENCY_VERSION_RELPATH',
    'AndroidDependencyPin',
    'ExternalDependencyVersions',
    'IosXcframeworkPin',
    'SdkVersionTable',
    'SdkVersionTableRow',
    'Version',
    'VersionBump',
    'modify_assemblyinfo',
    'modify_package_json',
    'read_android_dependency_pin',
    'read_external_dependency_versions',
    'read_ios_xcframework_pin',
    'write_android_dependency_pin',
    'write_ios_xcframework_pin',
]
