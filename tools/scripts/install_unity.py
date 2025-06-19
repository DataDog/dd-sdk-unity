import sys
import argparse

from common.log import init_logger
from common.unity_install import UnityHub, resolve_unity_install, match_unity_version


def install_unity(version_prefix: str):
    """
    Ensures that the given version of Unity is installed and registered in Unity Hub.

    The given `version_prefix` may specify an exact version (e.g. '2022.3.55f1'), or it
    may be a partial version: e.g. '2022.3' would signify any patch release of 2022.3,
    whereas '6000' would signify any Unity 6 release.

    If any already-installed version matches the desired version constraint, it will be
    used as-is and no new versions will be downloaded. If no matching version is yet
    installed, this script will download the latest matching release via Unity Hub.

    Returns 0 to indicate that a version matching the desired string is installed. If
    the command completes successfully, the final line printed to stdout is guaranteed
    to be the full, unambiguous version string of the installed version.

    Prerequisites: Unity Hub must already be installed on the system.
    """
    init_logger()

    # Check to see if we have the requisite version already installed, and if so, print
    # its full version string to stdout and exit
    unity_hub = UnityHub.require()
    unity_installs = unity_hub.list_installs()
    found_install = resolve_unity_install(unity_installs, version_prefix)
    if found_install:
        print(found_install.version)
        return 0

    # The target version is not installed; query the set of available versions and find
    # the newest one that satisfies the desired version
    versions = unity_hub.list_release_versions()
    version = match_unity_version(versions, version_prefix)
    if not version:
        raise RuntimeError(f'No available Unity release matches version {version_prefix}')

    # Commence installing this version, and when done, print its full version string to
    # stdout and exit
    new_install = unity_hub.install_version(version, ['ios', 'android'])
    print(new_install.version)
    return 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Ensures that the requisite version of Unity is installed via Unity Hub, installing it if needed.')
    parser.add_argument('version_prefix', help='The target version of Unity that must be installed; may be a partial specifier (e.g. "6000", "2023.3")')
    args = parser.parse_args()

    sys.exit(install_unity(args.version_prefix))
