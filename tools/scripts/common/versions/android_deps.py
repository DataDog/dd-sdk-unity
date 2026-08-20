"""
Utility code for reading and writing AndroidDependencyVersion.json, which pins the
dd-sdk-android version and the artifact IDs consumed by DatadogGradlePostProcessor when
it writes Gradle `implementation` declarations.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import os
import tempfile
import json
import io
from dataclasses import dataclass
from typing import IO, List

from common.log import get_default_logger

from .semver import Version


ANDROID_DEPENDENCY_VERSION_RELPATH = os.path.join('packages', 'Datadog.Unity', 'Editor', 'Android', 'AndroidDependencyVersion.json')


@dataclass
class AndroidDependencyPin:
    version: Version
    artifacts: List[str]


def read_android_dependency_pin(file_contents: str) -> AndroidDependencyPin:
    infile = io.StringIO(file_contents)
    return _read_android_dependency_pin_impl(infile)


def _read_android_dependency_pin_impl(infile: IO[str]) -> AndroidDependencyPin:
    doc = json.load(infile)

    if 'version' not in doc:
        raise RuntimeError('AndroidDependencyVersion.json is missing required key: version')
    if 'artifacts' not in doc:
        raise RuntimeError('AndroidDependencyVersion.json is missing required key: artifacts')
    if not doc['artifacts']:
        raise RuntimeError('AndroidDependencyVersion.json has an empty artifacts list')

    return AndroidDependencyPin(
        version=Version.parse(doc['version']),
        artifacts=list(doc['artifacts']),
    )


def write_android_dependency_pin(path: str, pin: AndroidDependencyPin):
    outfile_name = ''
    with tempfile.NamedTemporaryFile('w', delete=False) as outfile:
        outfile_name = outfile.name
        with open(path, 'r') as infile:
            _write_android_dependency_pin_impl(infile, outfile, pin)
    os.rename(outfile_name, path)


def _write_android_dependency_pin_impl(infile: IO[str], outfile: IO[str], pin: AndroidDependencyPin):
    log = get_default_logger()

    existing_doc = json.load(infile)
    existing_version = existing_doc.get('version')

    if existing_version == pin.version:
        log.info(f'Android dependency pin is already at {pin.version}.')
    else:
        log.info(f'Android dependency pin updated to {pin.version} (was {existing_version}).')

    doc = {
        'version': str(pin.version),
        'artifacts': list(pin.artifacts),
    }
    json.dump(doc, outfile, indent=2)
    outfile.write('\n')
