"""
Utility code for invoking sdkmanager, which manages installed Android SDK components.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import re
from dataclasses import dataclass
from typing import List

from common.shell import capture_output

from .util import resolve_android_binary


@dataclass
class AndroidPackage:
    path: str
    version: str
    description: str


class AndroidSdkManager(object):
    """
    Wrapper for $ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager.
    """
    path: str

    def __init__(self, path: str):
        self.path = path

    def list_installed_packages(self) -> List[AndroidPackage]:
        stdout, _ = capture_output(self.path, '--list')
        return _parse_sdkmanager_list_output(stdout)
    
    def install_package(self, package_path: str):
        capture_output(self.path, '--install', package_path)

    @classmethod
    def require(cls) -> 'AndroidSdkManager':
        path, error_message = resolve_android_binary('cmdline-tools', 'latest', 'bin', 'sdkmanager')
        if error_message:
            raise RuntimeError(f'Failed to find sdkmanager: {error_message}')
        return cls(path)


def _parse_sdkmanager_list_output(output: str) -> List[AndroidPackage]:
    lines = output.splitlines()

    section_header = 'Installed packages:'
    section_header_line_index = next((i for i, s in enumerate(lines) if s.startswith(section_header)), None)
    if section_header_line_index is None:
        raise RuntimeError(f'Unexpected sdkmanager output: "{section_header}" did not appear')
    section_lines = []
    for line in lines[section_header_line_index + 1:]:
        stripped_line = line.strip()
        if stripped_line.lower().startswith(('available packages:', 'available updates:')):
            break
        if not stripped_line:
            if section_lines:
                break
            continue
        section_lines.append(line)

    if not section_lines:
        return []

    if '|' in section_lines[0]:
        return _parse_pipe_delimited_packages(section_lines, section_header)
    return _parse_aligned_packages(section_lines, section_header)


def _parse_pipe_delimited_packages(section_lines: List[str], section_header: str) -> List[AndroidPackage]:
    if len(section_lines) < 2:
        raise RuntimeError(f'Unexpected sdkmanager output: incomplete table after "{section_header}"')

    def split_row(line: str) -> List[str]:
        return [s.strip() for s in line.split('|')]

    header_tokens = split_row(section_lines[0])
    if header_tokens != ['Path', 'Version', 'Description', 'Location']:
        raise RuntimeError(f'Unexpected sdkmanager output: "{section_header}" table has headings {", ".join(header_tokens)}')
    border_tokens = split_row(section_lines[1])
    if not all(column and set(column) == {'-'} for column in border_tokens):
        raise RuntimeError(f'Unexpected sdkmanager output: "{section_header}" table has no border after headings')

    # Parse the actual rows in the table
    packages: List[AndroidPackage] = []
    for line in section_lines[2:]:
        path, version, description, _ = split_row(line)
        packages.append(AndroidPackage(path=path, version=version, description=description))
    return packages


def _parse_aligned_packages(section_lines: List[str], section_header: str) -> List[AndroidPackage]:
    packages = []
    for line in section_lines:
        columns = re.split(r'\s{2,}', line.strip())
        if len(columns) < 3:
            raise RuntimeError(f'Unexpected sdkmanager output: malformed row after "{section_header}": {line}')

        path, version, *details = columns
        if '->' in version:
            version = version.split('->', maxsplit=1)[0].strip()
        elif len(details) >= 2 and details[0] == '->':
            details = details[2:]

        # The newer CLI prints paths with slashes; callers use SDK package IDs.
        path = path.replace('/', ';')
        packages.append(AndroidPackage(path=path, version=version, description=' '.join(details)))
    return packages
