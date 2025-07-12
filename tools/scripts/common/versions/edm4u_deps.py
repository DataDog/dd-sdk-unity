"""
Utility code for modifying DatadogDependencies.xml, which configures External
Dependency Manager for Unity (EDM4U) to pull in the requisite versions of
dd-sdk-android and dd-sdk-ios on Android and iOS builds, respectively.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import tempfile
import io
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from typing import IO, Optional

from common.log import get_default_logger

from .semver import Version


__ios_pods_xpath__ = './iosPods/iosPod'
__ios_pod_name_datadog_prefix__ = 'Datadog'

__android_packages_xpath__ = './androidPackages/androidPackage'
__android_package_spec_datadog_prefix__ = 'com.datadoghq'


@dataclass
class ExternalDependencyVersions:
    dd_sdk_android: Version
    dd_sdk_ios: Version


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


def _read_android_version(root: ET.Element) -> Version:
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

    # If we found a consistent version specifier, return it
    if version_str is None:
        raise RuntimeError('Failed to read Android package version(s)')
    return Version.parse(version_str)


def _read_ios_version(root: ET.Element) -> Version:
    # Iterate through all <iosPod> elements for Datadog packages, and read their
    # 'version' attributes
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
            
    # If we found a consistent version specifier, return it
    if version_str is None:
        raise RuntimeError('Failed to read iOS pod version(s)')
    return Version.parse(version_str)


def write_external_dependency_versions(path: str, versions: ExternalDependencyVersions):
    outfile_name = ''
    with tempfile.NamedTemporaryFile('wb', delete=False) as outfile:
        outfile_name = outfile.name
        with open(path, 'rb') as infile:
            _write_external_dependency_versions_impl(infile, outfile, versions)
    os.rename(outfile_name, path)


def _write_external_dependency_versions_impl(infile: IO[bytes], outfile: IO[bytes], versions: ExternalDependencyVersions):
    # Parse DatadogDependencies.xml, which configures EDM4U with our required external
    # dependencies
    tree = ET.parse(infile)
    root = tree.getroot()

    # Android: Update package specs for Gradle
    _mutate_android_version(root, versions.dd_sdk_android)
    _remove_android_snapshot_repositories(root)

    # iOS: Update 'version' constraint for CocoaPods
    _mutate_ios_version(root, versions.dd_sdk_ios)

    # Write the updated XML back to disk
    tree.write(outfile)


def _mutate_android_version(root: ET.Element, version: Version):
    # Iterate through all <androidPackage> elements and overwrite the version specifier
    # for all Datadog packages
    log = get_default_logger()
    for android_package_elem in root.findall(__android_packages_xpath__):
        spec = android_package_elem.get('spec', '')
        if spec.startswith(__android_package_spec_datadog_prefix__):
            tokens = spec.split(':')
            package_name = tokens[1]
            existing_version = tokens[2]
            if existing_version == version:
                log.info(f'Android package {package_name} is already at {version}.')
            else:
                new_spec = ':'.join(tokens[:-1] + [str(version)])
                android_package_elem.set('spec', new_spec)
                log.info(f'Android package {package_name} updated to {version} (was {existing_version}).')


def _remove_android_snapshot_repositories(root: ET.Element):
    # Check for any Android repositories with 'snapshots' in the URL and remove them
    repositories_elem = root.find('./androidPackages/repositories')
    if repositories_elem:
        for repository_elem in repositories_elem.findall('./repository'):
            if repository_elem.text and 'snapshots' in repository_elem.text:
                repositories_elem.remove(repository_elem)


def _mutate_ios_version(root: ET.Element, version: Version):
    # Iterate through all <iosPod> elements and update their 'version' attributes
    log = get_default_logger()
    for pod_elem in root.findall(__ios_pods_xpath__):
        name = pod_elem.get('name', '')
        if name.startswith(__ios_pod_name_datadog_prefix__):
            existing_version = pod_elem.get('version', '')
            if existing_version == version:
                log.info(f'iOS pod {name} is already at {version}.')
            else:
                pod_elem.set('version', str(version))
                log.info(f'iOS pod {name} updated to {version} (was {existing_version}).')
