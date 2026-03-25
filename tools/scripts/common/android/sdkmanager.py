"""
Utility code for invoking sdkmanager, which manages installed Android SDK components.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
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

    # Grab each line of the table that appears beneath 'Installed packages' in the output
    section_header = 'Installed packages:'
    section_header_line_index = next((i for i, s in enumerate(lines) if s.startswith(section_header)), None)
    if section_header_line_index is None:
        raise RuntimeError(f'Unexpected sdkmanager output: "{section_header}" did not appear')
    next_blank_line_index = next((i for i, s in enumerate(lines) if i > section_header_line_index and s.strip() == ''), None)
    if next_blank_line_index is None:
        raise RuntimeError(f'Unexpected sdkmanager output: found no blank line after "{section_header}"')
    section_lines = lines[section_header_line_index+1:next_blank_line_index]

    # Validate the format of the table
    if len(section_lines) < 2:
        raise RuntimeError(f'Unexpected sdkmanager output: unexpected blank line after "{section_header}"')

    def split_row(line: str) -> List[str]:
        return [s.strip() for s in line.split('|')]

    header_tokens = split_row(section_lines[0])
    if header_tokens != ['Path', 'Version', 'Description', 'Location']:
        raise RuntimeError(f'Unexpected sdkmanager output: "{section_header}" table has headings {", ".join(header_tokens)}')
    border_tokens = split_row(section_lines[1])
    if not all(s.count('-') == len(s) for s in border_tokens):
        raise RuntimeError(f'Unexpected sdkmanager output: "{section_header}" table has no border after headings')

    # Parse the actual rows in the table
    packages: List[AndroidPackage] = []
    for line in section_lines[2:]:
        path, version, description, _ = split_row(line)
        packages.append(AndroidPackage(path=path, version=version, description=description))
    return packages
