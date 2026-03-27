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
import argparse
from contextlib import contextmanager
from typing import List

from junitparser.junitparser import JUnitXml, TestCase

from common.log import init_logger
from common.unity import UnityHub, resolve_unity_install, UnityLicenseStatus, modified_ios_target_settings, restore_nuget_packages
from common.ddconfig import DatadogRuntimeConfig, FirstPartyHost, modified_datadog_settings
from common.inet_addr import get_reachable_inet_addr
from common.mockserver import prepare_mock_server_venv, run_mock_server
from common.simulator import run_default_simulator
from common.xslt import transform_nunit_to_junit


__repo_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

__default_test_project_root__ = os.path.join(__repo_root__, 'samples', 'Datadog Sample')
__default_test_project_unity_version__ = '2022'


@contextmanager
def _integration_test_env(project_path: str, config: DatadogRuntimeConfig, mock_server_bind_addr: str, mock_server_port: int, platform: str, target: str):
    # Modify DatadogSettings.asset to configure test prerequisites and set use the mock
    # server URL as a custom endpoint
    with modified_datadog_settings(project_path, config):
        # For iOS builds only, modify ProjectSettings.asset to use the desired iOS
        # target (simulator vs. device)
        with modified_ios_target_settings(project_path, platform, target):
            # Run the mock server that will record HTTP requests sent by the SDK
            with run_mock_server(mock_server_bind_addr, mock_server_port):
                # If we're targeting a real device, allow the tests to run: we expect
                # that the user has connected a physical phone that's the default
                # device for debugging
                if target != 'simulator':
                    yield
                    return

                # Similarly, proceed with tests if we're targeting iOS: Unity will
                # launch Xcode and run the tests through it, and Xcode automatically
                # launches a simulator unconditionally
                if platform == 'ios':
                    yield
                    return

                # For Android builds with a target of 'simulator', preemptively launch
                # an emulator running an appropriate AVD: this should become the
                # default device in adb, and Unity will deploy to it automatically
                # TODO: Device targeting is currently implicit; i.e. if a user already
                # has an Android phone connected or another simulator running, there's
                # no guarantee that we'll actually use this emulator for our tests
                with run_default_simulator(platform):
                    yield

def integration_test(unity_version_prefix: str, project_path: str, platform: str, target: str, out_junit_path_pattern: str):
    log = init_logger()

    # Make sure we have a Python interpreter with all required dependencies to run the
    # mock server
    prepare_mock_server_venv()

    # Check to see if we have the requisite Unity version installed
    unity_hub = UnityHub.require()
    unity_installs = unity_hub.list_installs()
    unity_install = resolve_unity_install(unity_installs, unity_version_prefix)
    if not unity_install:
        raise RuntimeError(f'No Unity version matching {unity_version_prefix} is installed')

    restore_nuget_packages(project_path, log)

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


    for abspath in [junit_abspath, nunit_abspath, log_abspath]:
        if os.path.isfile(abspath):
            log.info(f'Deleting old artifact: {abspath}')
            os.remove(abspath)


    # We need our mock server to bind to an address that is reachable from a simulator
    # or external device on our network
    mock_server_port = 5100
    mock_server_bind_addr = get_reachable_inet_addr()
    if not mock_server_bind_addr:
        raise RuntimeError('Failed to resolve a private IP for mock server')
    mock_server_url = f'http://{mock_server_bind_addr}:{mock_server_port}'

    # Temporarily modify the project's DatadogSettings asset so that it will send data
    # to the mock server we're about to stand up, and also apply any settings that the
    # integration tests require
    mock_host = f'{mock_server_bind_addr}:{mock_server_port}'
    config = DatadogRuntimeConfig(
        enabled=True,
        sdk_verbosity=1,
        client_token='fake-client-token',
        env='integration-test',
        service_name='datadog-sample',
        custom_endpoint=mock_server_url,
        batch_size=1,
        upload_frequency=1,
        batch_processing_level=1,
        crash_reporting_enabled=True,
        forward_unity_logs=True,
        remote_log_threshold=3,
        rum_enabled=True,
        rum_application_id='fake-rum-application-id',
        automatic_scene_tracking=True,
        session_sample_rate=100,
        trace_sample_rate=100,
        telemetry_sample_rate=100,
        # 18 == TracingHeaderType.Datadog | TracingHeaderType.TraceContext
        first_party_hosts=[FirstPartyHost(host=mock_host, tracing_header_type=18)],
    )
    with _integration_test_env(project_path, config, mock_server_bind_addr, mock_server_port, platform, target):
        # Run our Unity project's integration tests in the editor
        log.info(f'Running {platform} integration tests for project {os.path.basename(project_path)} in Unity {unity_install.version}...')
        args = [
            '-runTests',
            '-buildTarget', platform,
            '-testCategory', 'integration',
            '-testPlatform', platform,
            '-testResults', nunit_abspath,
        ]
        result = unity_install.run_batchmode(project_path, *args, log_path=log_abspath)
        if result.exitcode == 0:
            log.info('Tests finished successfully.')
        elif result.exitcode == 2:
            log.error('Tests failed.')
        elif result.license_status != UnityLicenseStatus.VALID:
            log.error('Unity failed to acquire a license.')
            return 86
        else:
            raise RuntimeError(f'Unity exited with status code {result.exitcode}')

        # Verify that fresh test results have been written to disk
        if not os.path.isfile(nunit_abspath):
            raise RuntimeError(f'Unity failed to write test results to {nunit_abspath}')

        # Convert the intermediate NUnit results file to JUnit format, and parse them
        transform_nunit_to_junit(nunit_abspath, junit_abspath)
        log.info(f'JUnit results written to: {junit_abspath}')
        os.remove(nunit_abspath)
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
        if failed_cases or result.exitcode == 2:
            log.error(f'{len(failed_cases)} of {num_passed + len(failed_cases)} tests failed:')
            for case in failed_cases:
                log.error(f'❌ {case.name}')
            return 2

        log.info(f'✅ {num_passed} tests passed ({num_skipped} skipped).')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Run integration tests on the provided version of Unity and on the specified platform.')
    parser.add_argument('--unity-version', '-u', default=__default_test_project_unity_version__, help='The target version of Unity to build with; may be a partial specifier (e.g. "6000", "2023.3")')
    parser.add_argument('--project', '-p', default=__default_test_project_root__, help="Path to the root directory of the Unity project to load; defaults to 'samples/Demo Data' in this repo")
    parser.add_argument('--platform', choices=['ios', 'android'], required=True, help='The platform to build an app bundle for')
    parser.add_argument('--target', choices=['simulator', 'device'], default='simulator', help="Whether to run on an emulated or physical device. If set to 'simulator' (default), this script will run the required emulator automatically; if set to 'device', your must have a phone connected and ready for debugging.")
    parser.add_argument('--out-junit-path-pattern', '-o', default='integration-test-%(platform)s.xml', help='Path where JUnit-formatted results will be written, relative to working directory')
    args = parser.parse_args()

    sys.exit(integration_test(args.unity_version, args.project, args.platform, args.target, args.out_junit_path_pattern))
