"""
Runs integration tests in a sample project for either iOS or Android, ensuring that the
mock server app is running, the project is configured appropriately to send the
requisite requests to the mock server, and any required device emulators are running.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import sys
import signal
import shutil
import argparse
from pathlib import Path
from contextlib import contextmanager
from typing import List

from junitparser.junitparser import JUnitXml, TestCase

from common.log import init_logger
from common.unity import UnityCli, resolve_unity_install, modified_ios_target_settings
from integration_test_setup import IntegrationTestEnvironment
from common.simulator import run_default_simulator
from common.xslt import transform_nunit_to_junit
from common.apple.simulator import run_simulator_tests


__repo_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

__default_test_project_root__ = os.path.join(__repo_root__, 'samples', 'Datadog Sample')
__default_test_project_unity_version__ = '2022'


@contextmanager
def _integration_test_env(project_path: str, platform: str, target: str, emulator_log_path: str, headless: bool):
    # Python owns a newly started server through export, native execution and results.
    # A healthy server started from the Editor is borrowed and left running.
    environment = IntegrationTestEnvironment(project_path)
    started_server = False
    owner = None
    try:
        started_server = environment.prepare()
        owner = environment._read_state()['owner']
        with modified_ios_target_settings(project_path, platform, target):
            if target != 'simulator' or platform == 'ios':
                yield
            else:
                with run_default_simulator(platform, log_path=emulator_log_path, headless=headless):
                    yield
    finally:
        try:
            # C# normally restores this snapshot. If Unity crashed or was killed,
            # restore the original bytes without interpreting the Unity asset.
            backup = Path(project_path) / 'Library/DatadogIntegrationTests/DatadogSettings.original'
            if backup.is_file():
                (Path(project_path) / 'Assets/Resources/DatadogSettings.asset').write_bytes(backup.read_bytes())
                backup.unlink()
        finally:
            if started_server:
                environment.finish(expected_owner=owner)

def integration_test(unity_version_prefix: str, project_path: str, platform: str, target: str, out_junit_path_pattern: str, headless: bool = False):
    log = init_logger()

    # Check to see if we have the requisite Unity version installed
    unity_cli = UnityCli.require()
    unity_installs = unity_cli.list_installs()
    unity_install = resolve_unity_install(unity_installs, unity_version_prefix)
    if not unity_install:
        raise RuntimeError(f'No Unity version matching {unity_version_prefix} is installed')

    # Ensure that our output path has a 'platform' placeholder
    if r'%(platform)s' not in out_junit_path_pattern:
        root, ext = os.path.splitext(out_junit_path_pattern)
        out_junit_path_pattern = root + r'-%(platform)s' + ext

    # Compute paths to artifact files
    junit_abspath = os.path.abspath(out_junit_path_pattern % {'platform': platform.lower()})
    artifact_dir, junit_filename = os.path.split(junit_abspath)
    junit_filename_noext, _ = os.path.splitext(junit_filename)
    nunit_abspath = os.path.join(artifact_dir, 'nunit-' + junit_filename)
    log_abspath = os.path.join(artifact_dir, junit_filename_noext + '.log')
    emulator_log_abspath = os.path.join(artifact_dir, junit_filename_noext + '-emulator.log')
    os.makedirs(artifact_dir, exist_ok=True)


    for abspath in [junit_abspath, nunit_abspath, log_abspath, emulator_log_abspath]:
        if os.path.isfile(abspath):
            log.info(f'Deleting old artifact: {abspath}')
            os.remove(abspath)


    with _integration_test_env(project_path, platform, target, emulator_log_abspath, headless):
        # GUI and Android use normal UTF; only script-driven iOS Simulator runs split.
        log.info(f'Running {platform} integration tests for project {os.path.basename(project_path)} in Unity {unity_install.version}...')
        build_target = {'android': 'Android', 'ios': 'iOS'}[platform]
        args = [
            '-runTests',
            '-buildTarget', build_target,
            '-testCategory', 'integration',
            '-testPlatform', build_target,
            '-testResults', nunit_abspath,
        ]
        split = platform == 'ios' and target == 'simulator'
        export_path = Path(project_path).resolve() / 'Build/DatadogIntegrationTests/PlayerWithTests'
        if split:
            if export_path.exists():
                shutil.rmtree(export_path)
            args += ['-buildPlayerPath', str(export_path.parent)]
        ci = os.environ.get('CI', '').lower() == 'true'
        if ci:
            args.append('-nographics')
        options = {'timeout_seconds': 8 * 60} if split else {}
        exitcode = unity_install.run_batchmode(project_path, *args, log_path=log_abspath, **options)
        if split:
            if exitcode != 0:
                raise RuntimeError(f'Unity export exited with status code {exitcode}')
            log.info('Unity export finished; compiling and running the test player.')
            run_simulator_tests(export_path, nunit_abspath,
                                Path(log_abspath).with_name(junit_filename_noext + '-native.log'))
        if exitcode not in (0, 2):
            raise RuntimeError(f'Unity exited with status code {exitcode}')

        # Verify that fresh test results have been written to disk
        if not os.path.isfile(nunit_abspath):
            raise RuntimeError(f'Unity failed to write test results to {nunit_abspath}')

        # Convert the intermediate NUnit results file to JUnit format, and parse them
        transform_nunit_to_junit(nunit_abspath, junit_abspath)
        log.info(f'JUnit results written to: {junit_abspath}')
        test_results = JUnitXml.fromfile(junit_abspath)

        # Summarize JUnit results in the console
        num_skipped = 0
        num_passed = 0
        failed_cases: List[TestCase] = []
        for suite in test_results:
            for case in suite:
                if case.is_skipped:
                    num_skipped += 1
                    continue
                if case.is_passed:
                    num_passed += 1
                    continue
                failed_cases.append(case)

        # If any tests failed, print a basic summary and propagate Unity's exit
        # code: do not proceed to testing additional platforms
        if failed_cases or exitcode == 2:
            log.error(f'{len(failed_cases)} of {num_passed + len(failed_cases)} tests failed:')
            for case in failed_cases:
                log.error(f'❌ {case.name}')
            return 2

        if num_passed == 0:
            raise RuntimeError('Unity reported no passing integration tests; check test discovery and platform filters')

        log.info(f'✅ {num_passed} tests passed ({num_skipped} skipped).')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Run integration tests on the provided version of Unity and on the specified platform.')
    parser.add_argument('--unity-version', '-u', default=__default_test_project_unity_version__, help='The target version of Unity to build with; may be a partial specifier (e.g. "6000", "2023.3")')
    parser.add_argument('--project', '-p', default=__default_test_project_root__, help="Path to the root directory of the Unity project to load; defaults to 'samples/Datadog Sample' in this repo")
    parser.add_argument('--platform', choices=['ios', 'android'], required=True, help='The platform to build an app bundle for')
    parser.add_argument('--target', choices=['simulator', 'device'], default='simulator', help="Whether to run on an emulated or physical device. If set to 'simulator' (default), this script will run the required emulator automatically; if set to 'device', your must have a phone connected and ready for debugging.")
    parser.add_argument('--out-junit-path-pattern', '-o', default='integration-test-%(platform)s.xml', help='Path where JUnit-formatted results will be written, relative to working directory')
    parser.add_argument('--headless', action='store_true', help='Run the Android emulator without a window')
    args = parser.parse_args()

    def interrupted(signum, frame):
        raise KeyboardInterrupt('Integration tests interrupted')

    signal.signal(signal.SIGTERM, interrupted)
    sys.exit(integration_test(args.unity_version, args.project, args.platform, args.target, args.out_junit_path_pattern, args.headless))
