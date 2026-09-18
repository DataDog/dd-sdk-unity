# Unless explicitly stated otherwise all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""
Library module (not a `./run-script` entrypoint) confirming dd-sdk-android artifact
reachability on Maven Central and detecting drift in the pinned transitive dependency
versions declared below. Android artifacts are never downloaded into the package —
Gradle resolves them at the user's build time — so there is no stage step here.
"""
import subprocess
from typing import Dict, List
from xml.etree import ElementTree as ET


MAVEN_BASE = 'https://repo1.maven.org/maven2'
MAVEN_GROUP_ID = 'com.datadoghq'
MAVEN_GROUP_PATH = 'com/datadoghq'

# Measured against the 3.10.0 pin; these are the transitive dependencies most likely to
# drift and cause a version conflict in a consuming project.
EXPECTED_TRANSITIVES = {
    'org.jetbrains.kotlin:kotlin-stdlib': '2.0.21',
    'com.squareup.okhttp3:okhttp': '4.12.0',
}


def pom_url(artifact: str, version: str) -> str:
    return f'{MAVEN_BASE}/{MAVEN_GROUP_PATH}/{artifact}/{version}/{artifact}-{version}.pom'


def aar_url(artifact: str, version: str) -> str:
    return f'{MAVEN_BASE}/{MAVEN_GROUP_PATH}/{artifact}/{version}/{artifact}-{version}.aar'


def fetch_text(log, url: str) -> str:
    log.info(f'Fetching {url}')
    # Equivalent to `curl --proto '=https' --tlsv1.2 --fail --location --silent
    # --show-error <url>`, passed as separate argv entries (no shell involved).
    # '=https' restricts curl to exactly the https:// protocol.
    result = subprocess.run(
        [
            'curl',
            '--proto', '=https',
            '--tlsv1.2',
            '--fail',
            '--location',
            '--silent',
            '--show-error',
            url,
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout


def check_reachable(log, url: str) -> bool:
    log.info(f'Checking reachability of {url}')
    result = subprocess.run(
        [
            'curl',
            '--proto', '=https',
            '--tlsv1.2',
            '--fail',
            '--location',
            '--silent',
            '--show-error',
            '--head',
            '--output', '/dev/null',
            url,
        ],
        check=False,
        capture_output=True,
        text=True,
    )
    return result.returncode == 0


def parse_direct_dependencies(pom_xml: str) -> Dict[str, str]:
    root = ET.fromstring(pom_xml)

    def local_tag(elem) -> str:
        tag = elem.tag
        if '}' in tag:
            return tag.split('}', 1)[1]
        return tag

    deps: Dict[str, str] = {}
    for elem in root.iter():
        if local_tag(elem) != 'dependencies':
            continue
        for dependency in elem:
            if local_tag(dependency) != 'dependency':
                continue
            group_id = None
            artifact_id = None
            version = None
            for child in dependency:
                child_tag = local_tag(child)
                if child_tag == 'groupId':
                    group_id = child.text
                elif child_tag == 'artifactId':
                    artifact_id = child.text
                elif child_tag == 'version':
                    version = child.text
            if not group_id or not artifact_id or not version:
                continue
            deps[f'{group_id}:{artifact_id}'] = version

    return deps


def verify_artifacts_reachable(log, artifacts: List[str], version: str) -> List[str]:
    unreachable: List[str] = []
    for artifact in artifacts:
        for url in (pom_url(artifact, version), aar_url(artifact, version)):
            if not check_reachable(log, url):
                log.warning(f'Not reachable: {url}')
                unreachable.append(url)
            else:
                log.info(f'Reachable: {url}')
    return unreachable


def collect_transitives(log, artifacts: List[str], version: str) -> Dict[str, str]:
    merged: Dict[str, str] = {}
    for artifact in artifacts:
        pom_xml = fetch_text(log, pom_url(artifact, version))
        deps = parse_direct_dependencies(pom_xml)
        for coordinate, dep_version in deps.items():
            if coordinate in merged and merged[coordinate] != dep_version:
                log.warning(
                    f'{coordinate} declared at {merged[coordinate]} by a previous artifact '
                    f'and at {dep_version} by {artifact}; keeping {merged[coordinate]}.'
                )
                continue
            merged[coordinate] = dep_version
    return merged


def check_transitive_drift(observed: Dict[str, str]) -> List[str]:
    drift: List[str] = []
    for coordinate, expected_version in EXPECTED_TRANSITIVES.items():
        observed_version = observed.get(coordinate, 'missing')
        if observed_version != expected_version:
            drift.append(f'{coordinate}: expected {expected_version}, found {observed_version}')
    return drift
