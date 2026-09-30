"""
Uses Unity CLI to install a target version of the Unity editor if it's not already
installed, optionally resolving a concrete version from a partial version constraint.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import sys
import argparse

from common.log import init_logger
from common.unity import UnityCli, resolve_unity_install


def install_unity(version_prefix: str):
    """
    Ensures that the given version of Unity and its iOS/Android modules are installed.

    The given `version_prefix` may specify an exact version (e.g. '2022.3.55f1'), or it
    may be a partial version: e.g. '2022.3' would signify any patch release of 2022.3,
    whereas '6000' would signify any Unity 6 release.

    Reuses an already-installed Editor matching the version constraint and ensures
    its required modules are installed. Otherwise, Unity CLI installs the latest
    matching release with those modules.

    Returns 0 to indicate that a version matching the desired string is installed. If
    the command completes successfully, the final line printed to stdout is guaranteed
    to be the full, unambiguous version string of the installed version.

    Prerequisites: Unity CLI must already be installed on the system.
    """
    init_logger()

    # Check to see if we have the requisite version already installed, and if so, print
    # its full version string to stdout and exit
    unity_cli = UnityCli.require()
    unity_installs = unity_cli.list_installs()
    found_install = resolve_unity_install(unity_installs, version_prefix)
    if found_install:
        # make sure all modules are properly installed for this version
        unity_cli.install_modules(found_install.version, ['ios', 'android'])
        print(found_install.version)
        return 0

    # Let Unity CLI resolve the requested version and install it with its modules.
    new_install = unity_cli.install_version(version_prefix, ['ios', 'android'])
    print(new_install.version)
    return 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Ensures that the requisite version of Unity is installed via Unity CLI, installing it if needed.')
    parser.add_argument('version_prefix', help='The target version of Unity that must be installed; may be a partial specifier (e.g. "6000", "2023.3")')
    args = parser.parse_args()

    sys.exit(install_unity(args.version_prefix))
