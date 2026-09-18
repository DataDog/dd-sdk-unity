#!/usr/bin/env python3
# Unless explicitly stated otherwise all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""
A single, deliberate, manual maintainer command that bumps the pinned dd-sdk-android
version. Re-verifies Maven Central reachability and re-checks the transitive
kotlin-stdlib/okhttp versions before rewriting
packages/Datadog.Unity/Editor/Android/AndroidDependencyVersion.json -- so a failed bump
never leaves the repo pinned to an unvalidated version.

Unlike iOS, there is NO staging step: Android artifacts are never vendored into the
package. Gradle resolves them by coordinate at the user's build time, so reachability
plus transitive drift checking is the full verification surface.

Usage (via the repo's run-script wrapper):
    ./run-script update_android_version <version> [--force] [--dry-run] [--allow-transitive-drift]
"""

import argparse
import os
import sys

import android_maven
from common.log import init_logger
from common.versions.android_deps import (
    ANDROID_DEPENDENCY_VERSION_RELPATH,
    AndroidDependencyPin,
    read_android_dependency_pin,
    write_android_dependency_pin,
)
from common.versions.semver import Version


REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PIN_PATH = os.path.join(REPO_ROOT, ANDROID_DEPENDENCY_VERSION_RELPATH)


def load_current_pin():
    if not os.path.exists(PIN_PATH):
        sys.exit(f'{PIN_PATH} not found; cannot resolve the currently pinned dd-sdk-android version.')
    with open(PIN_PATH, 'r', encoding='utf-8') as infile:
        return read_android_dependency_pin(infile.read())


def resolve_artifacts(current_pin):
    # Android's artifact set is fixed at the three IDs in the pin; there is no
    # per-invocation override use case (RESEARCH finding #4), unlike iOS's --modules.
    return list(current_pin.artifacts)


def format_drift_message(drift_lines):
    lines = '\n'.join(f'  - {line}' for line in drift_lines)
    return (
        f'Cannot bump: the following transitive coordinate(s) drifted from the recorded '
        f'expectations:\n{lines}\n'
        'The bump was aborted and the pin was left untouched. Review the drift above, then '
        're-run with --allow-transitive-drift once you have confirmed it is acceptable.'
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('version', help='The dd-sdk-android version to bump the pin to, e.g. 3.11.0.')
    parser.add_argument('--force', action='store_true', help='Re-run the bump even if the target version equals the current pin.')
    parser.add_argument(
        '--dry-run', action='store_true',
        help='Verify as normal, but print the pin that would be written instead of writing it.',
    )
    parser.add_argument(
        '--allow-transitive-drift', action='store_true',
        help='Proceed past a detected kotlin-stdlib/okhttp transitive version change after review.',
    )
    args = parser.parse_args()

    log = init_logger()

    # Step 1: parse the target version; fail loudly on a malformed argument.
    try:
        target_version = Version.parse(args.version)
    except ValueError as e:
        sys.exit(f'Invalid version argument {args.version!r}: {e}')

    # Step 2: no-op if the pin is already at the target version, unless --force.
    current_pin = load_current_pin()
    if current_pin.version == target_version and not args.force:
        log.info(f'Android dependency pin is already at {target_version}; nothing to do (pass --force to re-run anyway).')
        return

    artifacts = resolve_artifacts(current_pin)

    # Step 3: reachability. A bump whose artifacts are unreachable on Maven Central must
    # stop loudly, not silently pin a broken version.
    unreachable = android_maven.verify_artifacts_reachable(log, artifacts, str(target_version))
    if unreachable:
        sys.exit(
            f'Cannot bump to {target_version}: the following URL(s) are not reachable on Maven '
            f'Central: {", ".join(unreachable)}. The pin was left untouched.'
        )

    # Step 4: transitive drift. A silent kotlin-stdlib/okhttp version change must require an
    # explicit, logged maintainer override rather than moving the pin unnoticed.
    observed = android_maven.collect_transitives(log, artifacts, str(target_version))
    drift = android_maven.check_transitive_drift(observed)
    if drift:
        if not args.allow_transitive_drift:
            sys.exit(format_drift_message(drift))
        for line in drift:
            log.warning(line)

    # Step 5: no staging step. Unlike iOS, Android artifacts are referenced by coordinate
    # and resolved by Gradle at the user's build time -- they are never vendored into this
    # package -- so reachability plus drift checking above is the full verification surface.

    new_pin = AndroidDependencyPin(version=target_version, artifacts=artifacts)

    if args.dry_run:
        log.info(f'--dry-run: would write pin version={new_pin.version} artifacts={new_pin.artifacts}')
        return

    # Step 6: only now write the new pin.
    write_android_dependency_pin(PIN_PATH, new_pin)

    log.info(
        f'Updated {ANDROID_DEPENDENCY_VERSION_RELPATH}: {current_pin.version} -> {new_pin.version}.'
    )
    log.info('Note: NATIVE_SDK_VERSIONS.md and the changelog are updated by the release flow, not by this script.')


if __name__ == '__main__':
    main()
