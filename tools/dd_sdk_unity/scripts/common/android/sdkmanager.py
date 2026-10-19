# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for invoking sdkmanager, which manages installed Android SDK components.
"""
import os
import re
from dataclasses import dataclass
from pathlib import Path

from dd_sdk_unity.scripts.common.android.util import resolve_android_binary
from dd_sdk_unity.scripts.common.shell import capture_output


@dataclass
class AndroidPackage:
    path: str
    version: str
    description: str


class AndroidSdkManager:
    """
    Wrapper for $ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager.
    """
    path: str

    def __init__(self, path: str) -> None:
        self.path = path

    def list_installed_packages(self) -> list[AndroidPackage]:
        stdout, _ = capture_output(self.path, '--list_installed')
        return _parse_sdkmanager_list_output(stdout)
    
    def install_package(self, package_path: str) -> None:
        sdk_root = Path(self.path).parents[3]
        if not os.access(sdk_root, os.W_OK):
            raise PermissionError(
                f'Cannot install {package_path}: the Android SDK at {sdk_root} is not writable.')
        capture_output(self.path, '--install', package_path)

    @classmethod
    def require(cls) -> AndroidSdkManager:
        path, error_message = resolve_android_binary('cmdline-tools', 'latest', 'bin', 'sdkmanager')
        if error_message:
            raise RuntimeError(f'Failed to find sdkmanager: {error_message}')
        return cls(path)


def _parse_sdkmanager_list_output(output: str) -> list[AndroidPackage]:
    lines = output.splitlines()

    # Grab each line of the table that appears beneath 'Installed packages' in the output
    section_header = 'Installed packages:'
    section_header_line_index = next((i for i, s in enumerate(lines) if s.startswith(section_header)), None)
    if section_header_line_index is None:
        raise RuntimeError(f'Unexpected sdkmanager output: "{section_header}" did not appear')
    packages: list[AndroidPackage] = []
    for line in lines[section_header_line_index + 1:]:
        line = line.strip()
        if not line:
            if packages:
                break
            continue
        if line.endswith(':'):
            break
        # Older sdkmanager uses pipes and column headers. The Android CLI uses
        # aligned columns, slash-separated package paths, and optional update arrows.
        fields = [field.strip() for field in line.split('|')] if '|' in line else re.split(r'\s{2,}', line, maxsplit=2)
        if fields[0] == 'Path' or set(fields[0]) <= {'-'}:
            continue
        if len(fields) < 3:
            raise RuntimeError(f'Unexpected sdkmanager package row: {line}')
        path, version, description = fields[:3]
        path = path.replace('/', ';')
        version = version.split('->')[0].strip()
        description = re.sub(r'^->\s+\S+\s+', '', description)
        packages.append(AndroidPackage(path=path, version=version, description=description))
    return packages
