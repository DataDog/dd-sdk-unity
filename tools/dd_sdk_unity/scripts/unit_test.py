# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Runs the unit test suite for the Unity SDK's C# implementation layer.
"""
import argparse
import os
from pathlib import Path

from dd_sdk_unity.scripts.common import TestMode
from dd_sdk_unity.scripts.common.arguments import parse_test_filters
from dd_sdk_unity.scripts.common.log import init_logger
from dd_sdk_unity.scripts.common.test_results import report_test_results
from dd_sdk_unity.scripts.common.unity import UnityCli, resolve_unity_install
from dd_sdk_unity.scripts.common.unity.log import report_unity_failure

__repo_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))

__default_test_project_root__ = os.path.join(__repo_root__, 'samples', 'Datadog Sample')
__default_test_project_unity_version__ = '2022'


def unit_test(
    version_prefix: str,
    project_path: str,
    platforms: list[TestMode],
    output_dir: str,
    test_filters: list[str] | None = None,
) -> int:
    """
    Prerequisites: Unity CLI must be on PATH, and the target Unity Editor must be
    installed. If no fixed license is installed locally, the Unity Licensing Client
    must be configured to obtain a floating license.
    """
    log = init_logger()

    # Require at least one valid platform
    if not platforms:
        raise ValueError('No test platforms specified')

    # Check to see if we have the requisite Unity version installed
    unity_cli = UnityCli.require()
    unity_installs = unity_cli.list_installs()
    unity_install = resolve_unity_install(unity_installs, version_prefix)
    if not unity_install:
        raise RuntimeError(f'No Unity version matching {version_prefix} is installed')

    for platform in platforms:
        artifact_dir = Path(output_dir).resolve() / platform.lower()
        junit_abspath = str(artifact_dir / 'junit.xml')
        log_abspath = str(artifact_dir / 'unity.log')

        # Ensure that any stale artifacts from previous runs are cleaned up
        for abspath in [junit_abspath, log_abspath]:
            if os.path.isfile(abspath):
                log.debug(f'Deleting old artifact: {abspath}')
                os.remove(abspath)

        log.info(f'Starting test run ({platform}, Unity {unity_install.version}), log: {log_abspath}')
        os.makedirs(artifact_dir, exist_ok=True)
        exitcode = unity_cli.run_tests(
            unity_install.version, project_path, platform, junit_abspath, log_abspath,
            '-testCategory', '!integration', test_filters=test_filters,
        )
        if exitcode not in (0, 8):
            log.error(f'Test run failed (exit code {exitcode}).')
            report_unity_failure(log_abspath)
            return exitcode

        if report_test_results(junit_abspath, exitcode):
            return 8

    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description='Runs the Unity SDK\'s Unit Test suite against the given version of Unity running the specified project')
    parser.add_argument('--unity-version', '-u', default=__default_test_project_unity_version__, help='The target version of Unity to build with; may be a partial specifier (e.g. "6000", "2023.3")')
    parser.add_argument('--project', '-p', default=__default_test_project_root__, help="Path to the root directory of the Unity project to load; defaults to 'samples/Datadog Sample' in this repo")
    parser.add_argument('--platform', dest='platforms', action='append', choices=['EditMode', 'PlayMode'], help='Test modes to run; defaults to EditMode and PlayMode')
    parser.add_argument('--outputDir', dest='output_dir', default='build/test-results/unit', help='Directory for logs and results, relative to the working directory. Each mode gets a subdirectory containing junit.xml (default: build/test-results/unit).')
    parser.add_argument('-filter', dest='test_filters', type=parse_test_filters,
                        help='Comma-separated test names or patterns. Integration tests remain excluded.')
    args = parser.parse_args()

    return unit_test(args.unity_version, args.project, args.platforms or ['EditMode', 'PlayMode'], args.output_dir, args.test_filters)
