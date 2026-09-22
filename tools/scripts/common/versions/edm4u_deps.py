"""
Utility code for parsing the historical DatadogDependencies.xml manifest that used to
configure External Dependency Manager for Unity (EDM4U) to pull in dd-sdk-android (and,
previously, dd-sdk-ios) on Unity builds.

Neither this repo nor any release published from it writes DatadogDependencies.xml
anymore: the Android pin now lives in Editor/Android/AndroidDependencyVersion.json (see
android_deps.py) and the iOS pin lives in Editor/iOS/IosDependencyVersion.json (see
ios_xcframework_deps.py). This module is retained solely so release tooling can still
parse manifests from releases published before those moves; it exposes no write path.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import io
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from typing import IO, Optional

from .semver import Version


__ios_pods_xpath__ = './iosPods/iosPod'
__ios_pod_name_datadog_prefix__ = 'Datadog'

__android_packages_xpath__ = './androidPackages/androidPackage'
__android_package_spec_datadog_prefix__ = 'com.datadoghq'


@dataclass
class ExternalDependencyVersions:
    dd_sdk_android: Optional[Version]
    dd_sdk_ios: Optional[Version]


def read_external_dependency_versions(file_contents: str) -> ExternalDependencyVersions:
    infile = io.BytesIO(file_contents.encode())
    return _read_external_dependency_versions_impl(infile)


def _read_external_dependency_versions_impl(infile: IO[bytes]) -> ExternalDependencyVersions:
    tree = ET.parse(infile)
    root = tree.getroot()
    return ExternalDependencyVersions(
        dd_sdk_android=_read_android_version(root),
        dd_sdk_ios=_read_ios_version(root),
    )


# Current and future releases no longer ship DatadogDependencies.xml at all (the Android
# pin moved to Editor/Android/AndroidDependencyVersion.json). This function exists only
# to parse manifests from releases published before that move; a None result is expected
# for releases published after it. prepare_release.py still needs this read path to
# resolve older releases' Android versions, so only the write path was deleted here. Do
# not delete this function as dead code.
def _read_android_version(root: ET.Element) -> Optional[Version]:
    # Iterate through all <androidPackage> elements for Datadog dependencies, and read
    # the version specifier from the end of their package specs
    version_str: Optional[str] = None
    for android_package_elem in root.findall(__android_packages_xpath__):
        spec = android_package_elem.get('spec', '')
        if spec.startswith(__android_package_spec_datadog_prefix__):
            # Require that all packages have the same version specifier
            tokens = spec.split(':')
            existing_version = tokens[-1]
            if version_str is not None and existing_version != version_str:
                raise RuntimeError(f'Android packages have mismatched versions: {existing_version} != {version_str}')
            version_str = existing_version

    # No <androidPackage> elements found: expected for a manifest that never declared
    # Android dependencies, or one from a release published after the JSON pin moved.
    if version_str is None:
        return None
    return Version.parse(version_str)


def _read_ios_version(root: ET.Element) -> Optional[Version]:
    # Iterate through all <iosPod> elements for Datadog packages, and read their
    # 'version' attributes. Current manifests no longer declare any <iosPod>
    # elements (the iOS pin moved to IosDependencyVersion.json), so it's expected
    # for this to find none; this function is retained only to still be able to
    # parse manifests from releases published before that move.
    version_str: Optional[str] = None
    for pod_elem in root.findall(__ios_pods_xpath__):
        name = pod_elem.get('name', '')
        if name.startswith(__ios_pod_name_datadog_prefix__):
            existing_version = pod_elem.get('version')
            if not existing_version:
                raise RuntimeError(f'iOS pod {name} has no version specifier')
            if version_str is not None and existing_version != version_str:
                raise RuntimeError(f'iOS pods have mismatched versions: {existing_version} != {version_str}')
            version_str = existing_version

    # No <iosPod> elements found: this is the expected, current-schema case.
    if version_str is None:
        return None
    return Version.parse(version_str)
