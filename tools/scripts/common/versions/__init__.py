"""
Utility code for reading and writing the various file formats in the dd-sdk-unity
repository that encode version information.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
from .semver import Version, VersionBump
from .sdkversions import SdkVersionTable, SdkVersionTableRow
from .edm4u_deps import ExternalDependencyVersions, read_external_dependency_versions, write_external_dependency_versions
from .ios_xcframework_deps import IosXcframeworkPin, read_ios_xcframework_pin, write_ios_xcframework_pin, IOS_DEPENDENCY_VERSION_RELPATH
from .package import modify_package_json
from .assemblyinfo import modify_assemblyinfo


__all__ = [
    'Version',
    'VersionBump',
    'SdkVersionTable',
    'SdkVersionTableRow',
    'ExternalDependencyVersions',
    'read_external_dependency_versions',
    'write_external_dependency_versions',
    'IosXcframeworkPin',
    'read_ios_xcframework_pin',
    'write_ios_xcframework_pin',
    'IOS_DEPENDENCY_VERSION_RELPATH',
    'modify_package_json',
    'modify_assemblyinfo',
]
