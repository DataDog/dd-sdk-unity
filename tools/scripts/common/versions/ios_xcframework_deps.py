"""
Utility code for reading and writing IosDependencyVersion.json, which pins the
dd-sdk-ios prebuilt XCFramework version, its expected zip SHA-256, and the expected
module list consumed when the XCFramework is fetched and staged for iOS builds.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import tempfile
import json
import io
from dataclasses import dataclass
from typing import IO, List, Optional

from common.log import get_default_logger

from .semver import Version


IOS_DEPENDENCY_VERSION_RELPATH = os.path.join('packages', 'Datadog.Unity', 'Editor', 'iOS', 'IosDependencyVersion.json')


@dataclass
class IosXcframeworkPin:
    version: Version
    sha256: Optional[str]
    modules: List[str]


def read_ios_xcframework_pin(file_contents: str) -> IosXcframeworkPin:
    infile = io.StringIO(file_contents)
    return _read_ios_xcframework_pin_impl(infile)


def _read_ios_xcframework_pin_impl(infile: IO[str]) -> IosXcframeworkPin:
    doc = json.load(infile)

    if 'version' not in doc:
        raise RuntimeError('IosDependencyVersion.json is missing required key: version')
    if 'modules' not in doc:
        raise RuntimeError('IosDependencyVersion.json is missing required key: modules')
    if not doc['modules']:
        raise RuntimeError('IosDependencyVersion.json has an empty modules list')

    return IosXcframeworkPin(
        version=Version.parse(doc['version']),
        sha256=doc.get('sha256'),
        modules=list(doc['modules']),
    )


def write_ios_xcframework_pin(path: str, pin: IosXcframeworkPin):
    outfile_name = ''
    with tempfile.NamedTemporaryFile('w', delete=False) as outfile:
        outfile_name = outfile.name
        with open(path, 'r') as infile:
            _write_ios_xcframework_pin_impl(infile, outfile, pin)
    os.rename(outfile_name, path)


def _write_ios_xcframework_pin_impl(infile: IO[str], outfile: IO[str], pin: IosXcframeworkPin):
    log = get_default_logger()

    existing_doc = json.load(infile)
    existing_version = existing_doc.get('version')

    if existing_version == pin.version:
        log.info(f'iOS XCFramework pin is already at {pin.version}.')
    else:
        log.info(f'iOS XCFramework pin updated to {pin.version} (was {existing_version}).')

    doc = {
        'version': str(pin.version),
        'sha256': pin.sha256,
        'modules': list(pin.modules),
    }
    json.dump(doc, outfile, indent=2)
    outfile.write('\n')
