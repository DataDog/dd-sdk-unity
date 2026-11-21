# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Runs integration tests in a sample project's target player, ensuring that the
mock server app is running, the project is configured appropriately to send the
requisite requests to the mock server, and any required device emulators are running.
"""
import argparse
import json
import os
import shutil
import signal
from collections.abc import Generator
from contextlib import contextmanager
from pathlib import Path
from tempfile import TemporaryDirectory
from types import FrameType

from dd_sdk_unity.scripts.common import DeviceTarget, TargetPlatform
from dd_sdk_unity.scripts.common.apple.simulator import run_simulator_tests
from dd_sdk_unity.scripts.common.arguments import parse_test_filters
from dd_sdk_unity.scripts.common.log import init_logger
from dd_sdk_unity.scripts.common.simulator import run_default_simulator
from dd_sdk_unity.scripts.common.test_results import report_test_results
from dd_sdk_unity.scripts.common.unity import (
    UnityCli,
    resolve_unity_install,
)
from dd_sdk_unity.scripts.common.unity.log import report_unity_failure
from dd_sdk_unity.scripts.common.xslt import transform_nunit_to_junit
from dd_sdk_unity.scripts.integration_test_setup import IntegrationTestEnvironment
from dd_sdk_unity.tests.webgl import run_webgl_tests

__repo_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))

__default_test_project_root__ = os.path.join(__repo_root__, 'samples', 'Datadog Sample')
__default_test_project_unity_version__ = '2022'


@contextmanager
def _integration_test_env(project_path: str, platform: TargetPlatform, target: DeviceTarget) -> Generator[None]:
    # Keep the mock server alive through the complete player run.
    environment = IntegrationTestEnvironment(project_path)
    process = None
    prepared = False
    try:
        process = environment.start()
        prepared = True
        if platform == 'android' and target == 'simulator':
            with run_default_simulator(platform):
                yield
        else:
            yield
    finally:
        if prepared:
            environment.stop(process)

def integration_test(
    unity_version_prefix: str,
    project_path: str,
    platform: TargetPlatform,
    target: DeviceTarget,
    output_dir: str | None,
    test_filters: list[str] | None = None,
) -> int:
    log = init_logger()

    # Check to see if we have the requisite Unity version installed
    unity_cli = UnityCli.require()
    unity_installs = unity_cli.list_installs()
    unity_install = resolve_unity_install(unity_installs, unity_version_prefix)
    if not unity_install:
        raise RuntimeError(f'No Unity version matching {unity_version_prefix} is installed')

    artifact_dir = Path(output_dir or Path('build/test-results/integration') / platform).resolve()
    junit_abspath = str(artifact_dir / 'junit.xml')
    nunit_abspath = str(artifact_dir / 'nunit.xml')
    log_abspath = str(artifact_dir / 'unity.log')

    for abspath in [junit_abspath, nunit_abspath, log_abspath]:
        if os.path.isfile(abspath):
            log.debug(f'Deleting old artifact: {abspath}')
            os.remove(abspath)


    with _integration_test_env(project_path, platform, target):
        # Build and run our Unity project's integration tests in a player.
        build_target = {'ios': 'iOS', 'android': 'Android', 'webgl': 'WebGL', 'macos': 'StandaloneOSX'}[platform]
        args = [
            '-runTests',
            '-buildTarget', build_target,
            '-testCategory', 'integration',
            '-testPlatform', build_target,
            '-testResults', nunit_abspath,
            '-nographics',
        ]
        if test_filters:
            args += ['-testFilter', ';'.join(test_filters)]
        split = platform == 'webgl' or (platform == 'ios' and target == 'simulator')
        stage = 'Unity build' if split else 'build and test run'
        log.info(f'Starting {stage} ({platform}, Unity {unity_install.version}), log: {log_abspath}')
        if split:
            export_path = Path(project_path).resolve() / 'Build/DatadogIntegrationTests/PlayerWithTests'
            if export_path.exists():
                shutil.rmtree(export_path)
            export_path.parent.mkdir(parents=True, exist_ok=True)
            args += ['-buildPlayerPath', str(export_path.parent)]
        with TemporaryDirectory(prefix='datadog-integration-tests-') as settings_directory:
            if platform == 'ios':
                settings_path = Path(settings_directory) / 'TestSettings.json'
                settings_path.write_text(json.dumps({
                    'iOSTargetSDK': 'SimulatorSDK' if target == 'simulator' else 'DeviceSDK',
                }))
                args += ['-testSettingsFile', str(settings_path)]
            exitcode = unity_install.run_batchmode(project_path, *args, log_path=log_abspath)
        if split:
            if exitcode != 0:
                log.error(f'Unity build failed (exit code {exitcode}).')
                report_unity_failure(log_abspath)
                return exitcode
            log.info(f'Finished Unity build ({platform})')
            if platform == 'webgl':
                run_webgl_tests(export_path, nunit_abspath,
                                artifact_dir / 'browser.log')
            else:
                run_simulator_tests(export_path, nunit_abspath,
                                    artifact_dir / 'simulator.log')
        elif exitcode not in (0, 2):
            log.error(f'Build and test run failed (exit code {exitcode}).')
            report_unity_failure(log_abspath)
            return exitcode

        if not os.path.isfile(nunit_abspath):
            raise RuntimeError(f'Unity failed to write test results to {nunit_abspath}')
        transform_nunit_to_junit(nunit_abspath, junit_abspath)
        os.remove(nunit_abspath)
        if report_test_results(junit_abspath, exitcode):
            return 2

    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description='Run integration tests on the provided version of Unity and on the specified platform.')
    parser.add_argument('--unity-version', '-u', default=__default_test_project_unity_version__, help='The target version of Unity to build with; may be a partial specifier (e.g. "6000", "2023.3")')
    parser.add_argument('--project', '-p', default=__default_test_project_root__, help="Path to the root directory of the Unity project to load; defaults to 'samples/Datadog Sample' in this repo")
    parser.add_argument('--platform', choices=['ios', 'android', 'webgl', 'macos'], required=True, help='The platform to build a test player for')
    parser.add_argument('--target', choices=['simulator', 'device'], default='simulator', help="Mobile platforms only: whether to run on an emulated or physical device. If set to 'simulator' (default), this script will run the required emulator automatically; if set to 'device', you must have a phone connected and ready for debugging.")
    parser.add_argument('--outputDir', dest='output_dir', help='Directory for logs and junit.xml, relative to the working directory (default: build/test-results/integration/<platform>).')
    parser.add_argument('-filter', dest='test_filters', type=parse_test_filters,
                        help='Comma-separated test names or patterns. Category remains integration.')
    args = parser.parse_args()

    def interrupted(signum: int, frame: FrameType | None) -> None:
        raise KeyboardInterrupt('Integration tests interrupted')

    signal.signal(signal.SIGTERM, interrupted)
    return integration_test(args.unity_version, args.project, args.platform, args.target, args.output_dir, args.test_filters)
