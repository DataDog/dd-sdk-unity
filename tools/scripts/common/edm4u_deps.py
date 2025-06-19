import os
import tempfile
import xml.etree.ElementTree as ET
from typing import IO

from common.log import get_default_logger
from common.paths import repo_path
from common.types import ExternalDependencyVersions

__datadog_dependencies_xml__ = repo_path('packages', 'Datadog.Unity', 'Editor', 'DatadogDependencies.xml')


def write_external_dependency_versions(versions: ExternalDependencyVersions):
    outfile_name = ''
    with tempfile.NamedTemporaryFile('wb', delete=False) as outfile:
        outfile_name = outfile.name
        with open(__datadog_dependencies_xml__, 'rb') as infile:
            _write_external_dependency_versions_impl(infile, outfile, versions)
    os.rename(outfile_name, __datadog_dependencies_xml__)


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


def _mutate_android_version(root: ET.Element, version: str):
    # Iterate through all <androidPackage> elements and overwrite the version specifier
    # for all Datadog packages
    log = get_default_logger()
    for android_package_elem in root.findall('./androidPackages/androidPackage'):
        spec = android_package_elem.get('spec', '')
        if spec.startswith('com.datadoghq'):
            tokens = spec.split(':')
            package_name = tokens[1]
            existing_version = tokens[2]
            if existing_version == version:
                log.info(f'Android package {package_name} is already at {version}.')
            else:
                new_spec = ':'.join(tokens[:-1] + [version])
                android_package_elem.set('spec', new_spec)
                log.info(f'Android package {package_name} updated to {version} (was {existing_version}).')


def _remove_android_snapshot_repositories(root: ET.Element):
    # Check for any Android repositories with 'snapshots' in the URL and remove them
    repositories_elem = root.find('./androidPackages/repositories')
    if repositories_elem:
        for repository_elem in repositories_elem.findall('./repository'):
            if repository_elem.text and 'snapshots' in repository_elem.text:
                repositories_elem.remove(repository_elem)


def _mutate_ios_version(root: ET.Element, version: str):
    # Iterate through all <iosPod> elements and update their 'version' attributes
    log = get_default_logger()
    for pod_elem in root.findall('./iosPods/iosPod'):
        name = pod_elem.get('name', '')
        if name.startswith('Datadog'):
            existing_version = pod_elem.get('version', '')
            if existing_version == version:
                log.info(f'iOS pod {name} is already at {version}.')
            else:
                pod_elem.set('version', version)
                log.info(f'iOS pod {name} updated to {version} (was {existing_version}).')
