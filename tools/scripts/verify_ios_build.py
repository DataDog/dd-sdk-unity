#!/usr/bin/env python3
# Unless explicitly stated otherwise all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""
Re-runnable per-Unity-version driver that proves a Unity iOS build succeeds with the
prebuilt Datadog XCFramework embedded via
Unity's native Plugin importer alone, with EDM4U's iOS CocoaPods resolution permanently
absent. Stages the pinned XCFramework, runs a batch-mode Unity build via
IosBuildCommands.BuildIOS, asserts the generated pbxproj structure and the absence of
any CocoaPods artifacts, then runs xcodebuild and records a machine-readable result.

Usage (via the repo's run-script wrapper):
    ./run-script verify_ios_build --unity-version 2022.3
    ./run-script verify_ios_build --unity-version 2021.3 --out-json build/ios-verify/results.json
    ./run-script verify_ios_build --unity-version 6000 --keep-artifacts
"""

import argparse
import fcntl
import json
import os
import shutil
import subprocess
import sys
from dataclasses import asdict, dataclass, field
from typing import Dict, List, Optional

import ios_xcframework
from common.log import init_logger
from common.unity import UnityHub, UnityLicenseStatus, resolve_unity_install


REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

# These three pairs must match .gitlab-ci.yml's `unit-test` job matrix exactly.
MATRIX_RELATIVE_PATHS = [
    ('2021.3', 'test_scaffolds/2021 LTS'),
    ('2022.3', 'samples/Datadog Sample'),
    ('6000', 'test_scaffolds/6000 LTS'),
]
MATRIX: Dict[str, str] = {
    prefix: os.path.join(REPO_ROOT, *relative_path.split('/'))
    for prefix, relative_path in MATRIX_RELATIVE_PATHS
}

BUILD_METHOD = 'Datadog.Unity.BuildVerification.IosBuildCommands.BuildIOS'

DEFAULT_OUT_JSON = os.path.join(REPO_ROOT, 'build', 'ios-verify', 'results.json')
VERIFY_LOG_DIR = os.path.join(REPO_ROOT, 'build', 'ios-verify', 'logs')


@dataclass
class VerifyResult:
    unity_version_prefix: str
    resolved_version: Optional[str]
    project: str
    unity_build_ok: Optional[bool] = None
    modules_referenced: bool = False
    embed_frameworks_phase_present: bool = False
    framework_search_paths_present: bool = False
    cocoapods_absent: bool = False
    xcodebuild_succeeded: Optional[bool] = None
    license_blocked: bool = False
    blocked: bool = False
    blocked_reason: Optional[str] = None
    failure_excerpt: Optional[str] = None
    unity_log_path: Optional[str] = None
    xcodebuild_log_path: Optional[str] = None


def _read_tail(path: str, max_lines: int = 80) -> str:
    if not path or not os.path.isfile(path):
        return ''
    with open(path, 'r', encoding='utf-8', errors='replace') as infile:
        lines = infile.readlines()
    return ''.join(lines[-max_lines:])


def _check_project_not_locked(project_path: str):
    """
    Fails fast if a Unity Editor instance is already holding the project's lock file.
    Two concurrent Unity instances against the same project corrupt the build, so this
    check prevents that.
    """
    lock_path = os.path.join(project_path, 'Temp', 'UnityLockfile')
    if not os.path.isfile(lock_path):
        return

    try:
        fd = os.open(lock_path, os.O_RDWR)
    except OSError:
        return

    try:
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        fcntl.flock(fd, fcntl.LOCK_UN)
    except BlockingIOError:
        sys.exit(
            f'{project_path} appears to already be open in another Unity instance '
            f'(lock held on {lock_path}). Close it and retry; concurrent invocations '
            'against the same project corrupt the build.'
        )
    finally:
        os.close(fd)


def _check_pbxproj(pbxproj_path: str, module_names: List[str]) -> Dict[str, bool]:
    with open(pbxproj_path, 'r', encoding='utf-8', errors='replace') as infile:
        contents = infile.read()

    modules_referenced = all(f'{name}.xcframework' in contents for name in module_names)
    embed_phase_present = 'Embed Frameworks' in contents or 'PBXCopyFilesBuildPhase' in contents
    framework_search_paths_present = 'FRAMEWORK_SEARCH_PATHS' in contents

    checks = {
        'modules_referenced': modules_referenced,
        'embed_frameworks_phase_present': embed_phase_present,
        'framework_search_paths_present': framework_search_paths_present,
    }
    for name, value in checks.items():
        print(f'PBXPROJ_CHECK {name}: {str(value).lower()}')
    return checks


def _check_cocoapods_absent(ios_build_dir: str) -> bool:
    """
    Concrete, greppable evidence for ROADMAP criterion 4: no Podfile, no Pods/
    directory, and no Unity-iPhone.xcworkspace under the build output.
    """
    podfile = os.path.join(ios_build_dir, 'Podfile')
    pods_dir = os.path.join(ios_build_dir, 'Pods')
    workspace = os.path.join(ios_build_dir, 'Unity-iPhone.xcworkspace')

    absent = not os.path.exists(podfile) and not os.path.isdir(pods_dir) and not os.path.exists(workspace)
    print(f'PBXPROJ_CHECK cocoapods_absent: {str(absent).lower()}')
    return absent


def _run_xcodebuild(ios_build_dir: str, xcodebuild_log_path: str):
    # No -workspace, since no CocoaPods workspace is ever generated on this path:
    #   xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone \
    #       -configuration Release -destination 'generic/platform=iOS' \
    #       -derivedDataPath ./build CODE_SIGNING_ALLOWED=NO build
    # Note: the destination must be 'generic/platform=iOS', not 'generic/platform=iOS
    # Simulator' -- the generated scheme's SDKROOT is iphoneos-only, so a simulator
    # destination fails to resolve.
    xcodebuild_args = [
        'xcodebuild',
        '-project', 'Unity-iPhone.xcodeproj',
        '-scheme', 'Unity-iPhone',
        '-configuration', 'Release',
        '-destination', 'generic/platform=iOS',
        '-derivedDataPath', './build',
        'CODE_SIGNING_ALLOWED=NO',
        'build',
    ]

    # Capture xcodebuild's raw (non-beautified) output to the log file so the verbose
    # linker invocation (e.g. "-framework DatadogCore") is preserved for assertion.
    with open(xcodebuild_log_path, 'w', encoding='utf-8') as log_file:
        process = subprocess.Popen(
            xcodebuild_args, cwd=ios_build_dir,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
        )
        assert process.stdout is not None
        for line in process.stdout:
            print(line, end='')
            log_file.write(line)
        process.wait()
        if process.returncode != 0:
            raise subprocess.CalledProcessError(process.returncode, xcodebuild_args)


def verify_ios_build(version_prefix: str, project_path: str, keep_artifacts: bool) -> VerifyResult:
    log = init_logger()
    result = VerifyResult(
        unity_version_prefix=version_prefix,
        resolved_version=None,
        project=project_path,
    )

    # 1. Fail fast if the project is already open in another Unity instance.
    _check_project_not_locked(project_path)

    # 2. Resolve the Unity install for this version prefix.
    unity_hub = UnityHub.require()
    unity_installs = unity_hub.list_installs()
    unity_install = resolve_unity_install(unity_installs, version_prefix)
    if not unity_install:
        result.blocked = True
        result.blocked_reason = (
            f'No Unity install matching "{version_prefix}" found. '
            f'Run: ./run-script install_unity {version_prefix}'
        )
        log.error(result.blocked_reason)
        return result
    result.resolved_version = str(unity_install.version)
    log.info(f'Resolved Unity {version_prefix} -> {result.resolved_version}')

    # 3. Stage the pinned XCFramework subset into the package's own Plugins/iOS via
    # ios_xcframework's stage + verify (network fetch only, never Editor-side).
    pin = ios_xcframework.load_pin()
    ios_xcframework.fetch(log, str(pin.version), pin.sha256, force=False, allow_unknown_sha256=False)
    ios_xcframework.stage(log, pin.modules)
    ios_xcframework.verify(log, pin.modules)

    # 4. Delete any stale Build/iOS directory and log files before building. Unity
    # truncates -logFile in place (same inode) rather than replacing it, so a leftover
    # log from a prior run can make the tailer seek past the new run's own output and
    # misreport a licensing failure -- always start clean.
    ios_build_dir = os.path.join(project_path, 'Build', 'iOS')
    if os.path.isdir(ios_build_dir):
        log.info(f'Removing stale build directory: {ios_build_dir}')
        shutil.rmtree(ios_build_dir)

    version_slug = version_prefix.replace('.', '_')
    os.makedirs(VERIFY_LOG_DIR, exist_ok=True)
    unity_log_path = os.path.join(VERIFY_LOG_DIR, f'unity-{version_slug}.log')
    xcodebuild_log_path = os.path.join(VERIFY_LOG_DIR, f'xcodebuild-{version_slug}.log')
    result.unity_log_path = unity_log_path
    result.xcodebuild_log_path = xcodebuild_log_path

    for stale_log_path in (unity_log_path, xcodebuild_log_path):
        if os.path.isfile(stale_log_path):
            os.remove(stale_log_path)

    try:
        # 5. Run Unity in batch mode.
        absolute_output_dir = os.path.join(project_path, 'Build', 'iOS')
        batchmode_result = unity_install.run_batchmode(
            project_path, '-quit', '-executeMethod', BUILD_METHOD,
            '-iosBuildOutput', absolute_output_dir,
            log_path=unity_log_path,
        )

        if batchmode_result.exitcode == 0:
            log.info('Unity build finished successfully.')
            result.unity_build_ok = True
        elif batchmode_result.license_status != UnityLicenseStatus.VALID:
            log.error('Unity failed to acquire a license.')
            result.blocked = True
            result.license_blocked = True
            result.blocked_reason = 'Unity failed to acquire a valid license.'
            return result
        else:
            result.unity_build_ok = False
            result.failure_excerpt = _read_tail(unity_log_path)
            log.error(f'Unity build exited with status code {batchmode_result.exitcode}')
            return result

        # 6. Assert the generated Xcode project exists and record the pbxproj booleans.
        pbxproj_path = os.path.join(ios_build_dir, 'Unity-iPhone.xcodeproj', 'project.pbxproj')
        if not os.path.isfile(pbxproj_path):
            result.unity_build_ok = False
            result.failure_excerpt = f'Expected Xcode project not found at {pbxproj_path}'
            log.error(result.failure_excerpt)
            return result

        checks = _check_pbxproj(pbxproj_path, pin.modules)
        result.modules_referenced = checks['modules_referenced']
        result.embed_frameworks_phase_present = checks['embed_frameworks_phase_present']
        result.framework_search_paths_present = checks['framework_search_paths_present']
        result.cocoapods_absent = _check_cocoapods_absent(ios_build_dir)

        # 7. Run xcodebuild.
        try:
            _run_xcodebuild(ios_build_dir, xcodebuild_log_path)
            result.xcodebuild_succeeded = True
            log.info('xcodebuild finished successfully.')
        except Exception as exc:  # noqa: BLE001 - record any xcodebuild failure verbatim
            result.xcodebuild_succeeded = False
            result.failure_excerpt = _read_tail(xcodebuild_log_path) or str(exc)
            log.error(f'xcodebuild failed: {exc}')

        return result
    finally:
        # 8. Clean up: remove the generated build output and, unless --keep-artifacts,
        # the staged XCFramework modules. Never leave the project in a half-modified
        # state.
        if not keep_artifacts:
            if os.path.isdir(ios_build_dir):
                shutil.rmtree(ios_build_dir)
            ios_xcframework.clean(log)


def _merge_results_into_json(out_json: str, results: List[VerifyResult]):
    os.makedirs(os.path.dirname(out_json), exist_ok=True)
    existing: List[dict] = []
    if os.path.isfile(out_json):
        with open(out_json, 'r', encoding='utf-8') as infile:
            try:
                loaded = json.load(infile)
                existing = loaded if isinstance(loaded, list) else list(loaded.values())
            except json.JSONDecodeError:
                existing = []

    # Replace any prior record for the same version prefix so re-runs don't accumulate
    # stale duplicates, then append the new one(s).
    new_prefixes = {r.unity_version_prefix for r in results}
    existing = [r for r in existing if r.get('unity_version_prefix') not in new_prefixes]
    existing.extend(asdict(r) for r in results)

    with open(out_json, 'w', encoding='utf-8') as outfile:
        json.dump(existing, outfile, indent=2)


def main(unity_version: str, out_json: str, keep_artifacts: bool) -> int:
    log = init_logger()

    if unity_version not in MATRIX:
        sys.exit(
            f'"{unity_version}" is not one of the CI matrix versions {list(MATRIX.keys())}.'
        )
    project_path = MATRIX[unity_version]

    log.info(f'=== Verifying {unity_version} ({project_path}) ===')
    result = verify_ios_build(unity_version, project_path, keep_artifacts)
    print(f'DatadogIosBuildVerify:Result {json.dumps(asdict(result))}')

    _merge_results_into_json(out_json, [result])

    # Preserve this repo's established exit-code convention (see build_demo.py,
    # unit_test.py): a Unity license failure returns 86 so GitLab CI's
    # `retry.exit_codes: [86]` picks it up as a retryable environment issue rather than
    # a real build failure.
    if result.license_blocked:
        return 86

    if result.blocked:
        return 1

    all_green = (
        result.unity_build_ok
        and result.modules_referenced
        and result.embed_frameworks_phase_present
        and result.framework_search_paths_present
        and result.cocoapods_absent
        and result.xcodebuild_succeeded
    )
    return 0 if all_green else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        '--unity-version', required=True, choices=list(MATRIX.keys()),
        help='Unity version prefix to build (must be one of the CI matrix keys).',
    )
    parser.add_argument(
        '--out-json', default=DEFAULT_OUT_JSON,
        help='Path to merge the machine-readable result record into (default: build/ios-verify/results.json).',
    )
    parser.add_argument(
        '--keep-artifacts', action='store_true',
        help='Do not clean up the generated Xcode project or staged XCFramework modules on exit.',
    )
    args = parser.parse_args()

    sys.exit(main(args.unity_version, args.out_json, args.keep_artifacts))
