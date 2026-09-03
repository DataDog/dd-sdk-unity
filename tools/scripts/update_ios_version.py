#!/usr/bin/env python3
# Unless explicitly stated otherwise all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""
A single, deliberate, manual maintainer command that bumps the pinned dd-sdk-ios
XCFramework version. Fetches the target version, re-verifies its module set and stages
it, and only then rewrites packages/Datadog.Unity/Editor/iOS/IosDependencyVersion.json --
so a failed bump never leaves the repo pinned to an unvalidated version.

Usage (via the repo's run-script wrapper):
    ./run-script update_ios_version <version> [--force] [--modules a,b,c] [--dry-run]
"""

import argparse
import os
import sys

import ios_xcframework
from common.log import init_logger
from common.versions.ios_xcframework_deps import (
    IOS_DEPENDENCY_VERSION_RELPATH,
    IosXcframeworkPin,
    read_ios_xcframework_pin,
    write_ios_xcframework_pin,
)
from common.versions.semver import Version


REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PIN_PATH = os.path.join(REPO_ROOT, IOS_DEPENDENCY_VERSION_RELPATH)


def load_current_pin():
    if not os.path.exists(PIN_PATH):
        sys.exit(f'{PIN_PATH} not found; cannot resolve the currently pinned dd-sdk-ios version.')
    with open(PIN_PATH, 'r', encoding='utf-8') as infile:
        return read_ios_xcframework_pin(infile.read())


def find_missing_modules(required_modules, bundle_inventory):
    bundle_set = set(bundle_inventory)
    return [module for module in required_modules if module not in bundle_set]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('version', help='The dd-sdk-ios version to bump the pin to, e.g. 3.12.0.')
    parser.add_argument('--force', action='store_true', help='Re-run the bump even if the target version equals the current pin.')
    parser.add_argument(
        '--modules', default=None,
        help='Comma-separated override of the vendored module list (defaults to carrying forward the current pin\'s modules).',
    )
    parser.add_argument(
        '--dry-run', action='store_true',
        help='Fetch, verify, and stage as normal, but print the pin that would be written instead of writing it.',
    )
    args = parser.parse_args()

    log = init_logger()

    # Step 1: parse the target version; fail loudly on a malformed argument.
    try:
        target_version = Version.parse(args.version)
    except ValueError as e:
        sys.exit(f'Invalid version argument {args.version!r}: {e}')

    current_pin = load_current_pin()

    # Step 2: no-op if the pin is already at the target version, unless --force.
    if current_pin.version == target_version and not args.force:
        log.info(f'iOS XCFramework pin is already at {target_version}; nothing to do (pass --force to re-run anyway).')
        return

    requested_modules = args.modules.split(',') if args.modules else list(current_pin.modules)

    # Step 3: fetch the target version's zip. Its digest is by definition not yet
    # pinned, so unknown-digest is explicitly allowed here; the freshly computed digest
    # becomes the new pinned value in step 6.
    digest = ios_xcframework.fetch(
        log, str(target_version), expected_sha256=None, force=True, allow_unknown_sha256=True,
    )

    # Step 4: re-verify the module set against the bundle's actual inventory. A dd-sdk-ios
    # release that drops or renames a module must stop the bump loudly, not silently pin
    # a broken version.
    bundle_inventory = ios_xcframework.list_zip_modules(ios_xcframework.zip_path_for(target_version))
    missing = find_missing_modules(requested_modules, bundle_inventory)
    if missing:
        sys.exit(
            f'Cannot bump to {target_version}: module(s) {", ".join(missing)} not found in the fetched bundle. '
            f'Full bundle inventory: {", ".join(bundle_inventory)}.'
        )

    newly_added = sorted(set(bundle_inventory) - set(requested_modules))
    if newly_added:
        log.info(
            f'Note: the fetched bundle also contains module(s) not currently vendored: {", ".join(newly_added)}. '
            'Pass --modules to include them if desired.'
        )

    # Step 5: stage into Plugins/iOS and structurally verify before the pin moves.
    ios_xcframework.stage(log, str(target_version), requested_modules)
    ios_xcframework.verify(log, requested_modules)

    new_pin = IosXcframeworkPin(version=target_version, sha256=digest, modules=requested_modules)

    if args.dry_run:
        log.info(f'--dry-run: would write pin version={new_pin.version} sha256={new_pin.sha256} modules={new_pin.modules}')
        return

    # Step 6: only now write the new pin.
    write_ios_xcframework_pin(PIN_PATH, new_pin)

    log.info(
        f'Updated {IOS_DEPENDENCY_VERSION_RELPATH}: {current_pin.version} -> {new_pin.version} '
        f'(sha256={new_pin.sha256}).'
    )
    log.info('Note: NATIVE_SDK_VERSIONS.md and the changelog are updated by the release flow, not by this script.')


if __name__ == '__main__':
    main()
