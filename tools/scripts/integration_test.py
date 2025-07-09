import os
import sys
import argparse
from typing import List

from junitparser.junitparser import JUnitXml, TestCase

from common.log import init_logger
from common.unity import UnityHub, resolve_unity_install, UnityLicenseStatus
from common.ddconfig import DatadogRuntimeConfig, modified_datadog_settings
from common.mockserver import prepare_mock_server_venv, run_mock_server
from common.simulator import run_default_simulator
from common.xslt import transform_nunit_to_junit


__repo_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

__default_test_project_root__ = os.path.join(__repo_root__, 'samples', 'Datadog Sample')
__default_test_project_unity_version__ = '2022'


def integration_test(unity_version_prefix: str, project_path: str, platform: str, mock_server_port: int, out_junit_path_pattern: str):
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

    # Ensure that any stale artifacts from previous runs are cleaned up
    for abspath in [junit_abspath, nunit_abspath, log_abspath]:
        if os.path.isfile(abspath):
            log.info(f'Deleting old artifact: {abspath}')
            os.remove(abspath)
    
    # Temporarily modify the project's DatadogSettings asset so that it will send data
    # to the mock server we're about to stand up
    config = DatadogRuntimeConfig(
        custom_endpoint=f'http://localhost:{mock_server_port}',
        client_token='fake-client-token',
        rum_application_id='fake-rum-application-id',
    )
    with modified_datadog_settings(project_path, config):
        with run_mock_server(mock_server_port, prefer_localhost=True) as mock:
            with run_default_simulator(platform):
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

                log.info(f'{num_passed} tests passed ({num_skipped} skipped).')

            # TODO: Inspect the requests sent to the mock server
            mock.get()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Ensures that mock_server has all required schemas along with a properly initialized Python venv')
    parser.add_argument('--unity-version', '-u', default=__default_test_project_unity_version__, help='The target version of Unity to build with; may be a partial specifier (e.g. "6000", "2023.3")')
    parser.add_argument('--project', '-p', default=__default_test_project_root__, help="Path to the root directory of the Unity project to load; defaults to 'samples/Demo Data' in this repo")
    parser.add_argument('--platform', choices=['ios', 'android'], required=True, help='The platform to build an app bundle for')
    parser.add_argument('--mock-server-port', type=int, default=5100)
    parser.add_argument('--out-junit-path_pattern', '-o', default='integration-test-%(platform)s.xml', help='Path where JUnit-formatted results will be written, relative to working directory')
    args = parser.parse_args()

    sys.exit(integration_test(args.unity_version, args.project, args.platform, args.mock_server_port, args.out_junit_path_pattern))
