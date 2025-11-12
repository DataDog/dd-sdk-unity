"""
Uses Unity Hub to install a target version of the Unity editor if it's not already
installed, optionally resolving a concrete version from a partial version constraint.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import sys
import argparse

from common.log import init_logger
from common.unity import UnityHub, resolve_unity_install, match_unity_version


def install_unity(version: str, changeset: str, force: bool):
    """
    Ensures that the given version of Unity is installed and registered in Unity Hub,
    given its exact version string and corresponding changeset hash.

    Prerequisites: Unity Hub must already be installed on the system.
    """
    init_logger()

    unity_hub = UnityHub.require()

    # Check to see if we have the requisite version already installed, and if so, print
    # its full version string to stdout and exit
    if not force:
        unity_installs = unity_hub.list_installs()
        found_install = resolve_unity_install(unity_installs, version)
        if found_install:
            print(found_install.version)
            return 0

    # Commence installing this version, and when done, print its full version string to
    # stdout and exit
    new_install = unity_hub.install_version(version, changeset, ['ios', 'android'])
    print(new_install.version)
    return 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Ensures that the requisite version of Unity is installed via Unity Hub, installing it if needed.')
    parser.add_argument('version', help='The target version of Unity that must be installed, e.g. 2022.3.67f2')
    parser.add_argument('changeset', help='The corresponding changeset for that version, e.g. 6bedba8691df')
    parser.add_argument('--force', '-f', action='store_true', help='If set, perform install without checking for existing install')
    args = parser.parse_args()

    sys.exit(install_unity(args.version, args.changeset, args.force))
