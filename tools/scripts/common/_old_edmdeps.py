import logging
import xml.etree.ElementTree as ET
from enum import Enum
from typing import Optional

from common.paths import repo_path

log = logging.getLogger(__name__)

__datadog_dependencies_xml__ = repo_path('packages', 'Datadog.Unity', 'Editor', 'DatadogDependencies.xml')

__android_packages_xpath__ = './androidPackages/androidPackage'
__android_package_spec_datadog_prefix__ = 'com.datadoghq'

__ios_pods_xpath__ = './iosPods/iosPod'
__ios_pod_name_datadog_prefix__ = 'Datadog'


class ExternalDependency(Enum):
    DD_SDK_ANDROID = 1
    DD_SDK_IOS = 2


def read_version(dep: ExternalDependency) -> str:
    tree = ET.parse(__datadog_dependencies_xml__)
    root = tree.getroot()
    if dep == ExternalDependency.DD_SDK_ANDROID:
        return _read_android_version(root)
    elif dep == ExternalDependency.DD_SDK_IOS:
        return _read_ios_version(root)
    raise TypeError('unexpected ExternalDependency enum')


def write_version(dep: ExternalDependency, version: str):
    tree = ET.parse(__datadog_dependencies_xml__)
    root = tree.getroot()
    if dep == ExternalDependency.DD_SDK_ANDROID:
        _mutate_android_version(root, version)
        _remove_android_snapshot_repositories(root)
    elif dep == ExternalDependency.DD_SDK_IOS:
        _mutate_ios_version(root, version)
    else:
        raise TypeError('unexpected ExternalDependency enum')
    tree.write(__datadog_dependencies_xml__)


def _read_android_version(root: ET.Element[str]) -> str:
    # Iterate through all <androidPackage> elements for Datadog dependencies, and read
    # the version specifier from the end of their package specs
    version: Optional[str] = None
    for android_package_elem in root.findall(__android_packages_xpath__):
        spec = android_package_elem.get('spec', '')
        if spec.startswith(__android_package_spec_datadog_prefix__):
            # Require that all packages have the same version specifier
            tokens = spec.split(':')
            existing_version = tokens[-1]
            if version is not None and existing_version != version:
                raise RuntimeError(f'Android packages have mismatched versions: {existing_version} != {version}')
            version = existing_version

    # If we found a consistent version specifier, return it
    if version is None:
        raise RuntimeError('Failed to read Android package version(s)')
    return version


def _mutate_android_version(root: ET.Element[str], version: str):
    # Iterate through all <androidPackage> elements and overwrite the version specifier
    # for all Datadog packages
    for android_package_elem in root.findall(__android_packages_xpath__):
        spec = android_package_elem.get('spec', '')
        if spec.startswith(__android_package_spec_datadog_prefix__):
            tokens = spec.split(':')
            package_name = tokens[1]
            existing_version = tokens[2]
            if existing_version == version:
                log.info(f'Android package {package_name} is already at {version}.')
            else:
                log.info(f'Android package {package_name} updated to {version} (was {existing_version}).')


def _remove_android_snapshot_repositories(root: ET.Element[str]):
    # Check for any Android repositories with 'snapshots' in the URL and remove them
    repositories_elem = root.find('./androidPackages/repositories')
    if repositories_elem:
        for repository_elem in repositories_elem.findall('./repository'):
            if repository_elem.text and 'snapshots' in repository_elem.text:
                repositories_elem.remove(repository_elem)


def _read_ios_version(root: ET.Element[str]) -> str:
    # Iterate through all <iosPod> elements for Datadog packages, and read their
    # 'version' attributes
    version: Optional[str] = None
    for pod_elem in root.findall(__ios_pods_xpath__):
        name = pod_elem.get('name', '')
        if name.startswith(__ios_pod_name_datadog_prefix__):
            existing_version = pod_elem.get('version')
            if not existing_version:
                raise RuntimeError(f'iOS pod {name} has no version specifier')
            if version is not None and existing_version != version:
                raise RuntimeError(f'iOS pods have mismatched versions: {existing_version} != {version}')
            version = existing_version

    # If we found a consistent version specifier, return it
    if version is None:
        raise RuntimeError('Failed to read iOS pod version(s)')
    return version


def _mutate_ios_version(root: ET.Element[str], version: str):
    # Iterate through all <iosPod> elements and update their 'version' attributes
    for pod_elem in root.findall(__ios_pods_xpath__):
        name = pod_elem.get('name', '')
        if name.startswith(__ios_pod_name_datadog_prefix__):
            existing_version = pod_elem.get('version', '')
            if existing_version == version:
                log.info(f'iOS pod {name} is already at {version}.')
            else:
                log.info(f'iOS pod {name} updated to {version} (was {existing_version}).')
