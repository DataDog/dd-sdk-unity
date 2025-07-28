"""
Utility code for invoking aapt to examine .apk files.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import re
from typing import Optional

from ..shell import capture_output


def get_package_name(apk_path: str) -> str:
    # Check $ANDROID_HOME for available versions of build-tools
    android_home = os.getenv('ANDROID_HOME')
    if not android_home:
        raise RuntimeError('ANDROID_HOME is not set')

    build_tools = os.path.join(android_home, 'build-tools')
    if not os.path.isdir(build_tools):
        raise RuntimeError('$ANDROID_HOME/build-tools does not exist')

    versions = [s for s in os.listdir(build_tools) if re.match(r'\d+\.\d+\.\d+', s)]
    if not versions:
        raise RuntimeError('$ANDROID_HOME/build-tools contains no valid versions')
    
    # We're just checking basic .apk metadata; we don't need to worry about matching
    # the vesion used for our build: just pick the newest version
    versions.sort()
    version = versions[-1]

    # Resolve the path to aapt
    aapt_bin = os.path.join(build_tools, version, 'aapt')
    if not os.path.isfile(aapt_bin):
        raise RuntimeError(f'aapt binary not found at {aapt_bin}')
    
    # Run 'aapt dump badging' and parse the package name
    output, _ = capture_output(aapt_bin, 'dump', 'badging', apk_path)
    lines = output.splitlines()
    package_line = next((line for line in lines if line.startswith('package: ')), '')
    package_name_match = re.search(r"\bname='([^']+)'", package_line)
    if not package_name_match:
        raise RuntimeError(f'Failed to parse package name from aapt output: {output}')
    
    return package_name_match.group(1)
