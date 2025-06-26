import os
import sys
import argparse
from typing import List

from junitparser.junitparser import JUnitXml, TestCase

from common.log import init_logger
from common.unity import UnityHub, resolve_unity_install, UnityLicenseStatus
from common.xslt import transform_nunit_to_junit


def unit_test(version_prefix: str, project_path: str, platforms: List[str], out_junit_path_pattern: str):
    """
    Prerequisites: Unity Hub must be installed on the system, and the target version of
    Unity Editor must be installed through Unity Hub. If no fixed license is installed
    locally, the Unity Licensing Client must be configured to obtain a floating license.
    """
    log = init_logger()

    # Require at least one valid platform
    if not platforms:
        raise ValueError('No test platforms specified')

    # Ensure that our output path has a 'platform' placeholder
    if r'%(platform)s' not in out_junit_path_pattern:
        root, ext = os.path.splitext(out_junit_path_pattern)
        out_junit_path_pattern = root + r'-%(platform)s' + ext

    # Check to see if we have the requisite Unity version installed
    unity_hub = UnityHub.require()
    unity_installs = unity_hub.list_installs()
    unity_install = resolve_unity_install(unity_installs, version_prefix)
    if not unity_install:
        raise RuntimeError(f'No Unity version matching {version_prefix} is installed')

    for platform in platforms:
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

        log.info(f'Running {platform} unit tests for project {os.path.basename(project_path)} in Unity {unity_install.version}...')
        args = [
            '-runTests',
            '-testCategory', '!integration',
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

    log.info('Unit tests completed OK.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Runs the Unity SDK\'s Unit Test suite against the given version of Unity running the specified project')
    parser.add_argument('--unity-version', '-u', required=True, help='The target version of Unity to run; may be a partial specifier (e.g. "6000", "2023.3")')
    parser.add_argument('--project', '-p', required=True, help='Path to the root directory of the Unity project to load')
    parser.add_argument('--platform', dest='platforms', action='append', default=['PlayMode'], help='Platforms to test, e.g. EditMode, PlayMode, or a supported build platform')
    parser.add_argument('--out-junit-path-pattern', '-o', default='unit-test-%(platform)s.xml', help='Path where JUnit-formatted results will be written, relative to working directory')
    args = parser.parse_args()

    sys.exit(unit_test(args.unity_version, args.project, args.platforms, args.out_junit_path_pattern))
