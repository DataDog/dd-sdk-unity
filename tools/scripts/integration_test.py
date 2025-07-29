"""
Given a Unity project and a target platform, generates a packaged build that includes
test assemblies and that is configured to run Datadog SDK integration tests on startup,
then runs that build against a mock server and verifies that all tests passed.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import re
import sys
import time
import argparse
import logging
from datetime import timedelta
from dataclasses import dataclass, field
from typing import List, Optional

from common.log import init_logger
from common.unity import UnityProject, UnityBuild, UnityBuildPlatform, UnityBuildConfig, UnityTarget, DatadogBackendType
from common.inet_addr import get_reachable_inet_addr
from common.mockserver import run_mock_server, prepare_mock_server_venv
from common.device import acquire_device, __default_ios_device__
from common.android import Adb

__repo_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
__default_project_name__ = 'Datadog Sample'
__default_project_root__ = os.path.join(__repo_root__, 'samples', __default_project_name__)


@dataclass
class IntegrationTestResult:
    passed: bool
    duration: timedelta
    error_message: str = ''
    stack_trace: str = ''


@dataclass
class IntegrationTestMethod:
    name: str
    invoked: bool = False
    result: Optional[IntegrationTestResult] = None


@dataclass
class IntegrationTestType:
    full_name: str
    methods: List[IntegrationTestMethod] = field(default_factory=list)


@dataclass
class IntegrationTestRunnerOutputHandler(object):
    regex = re.compile(r':: IntegrationTestRunner \[([A-Z0-9:_-]+)\] (.*)')

    types: List[IntegrationTestType] = field(default_factory=list)
    exit_result = ''
    last_error = ''

    @property
    def should_exit(self) -> bool:
        return bool(self.last_error) or bool(self.exit_result)
    
    def report(self, log: logging.Logger) -> bool:
        if not self.should_exit:
            log.warning('⚠️ Integration tests aborted prematurely.')
            return False
        
        if self.last_error:
            log.error(f'❌ Error running integration tests: {self.last_error}')
            return False
                
        num_seen = 0
        num_passed = 0
        for type in self.types:
            for method in type.methods:
                fqn = f'{type.full_name}.{method.name}'
                num_seen += 1

                if not method.invoked or not method.result:
                    log.warning(f'- ⚠️ {fqn}: no output detected.')
                    continue

                if not method.result.passed:
                    log.error(f'- ❌ [FAIL] {fqn}: ({method.result.duration.total_seconds():0.2f}s)')
                    for line in method.result.error_message.splitlines():
                        log.error(line)
                    if method.result.stack_trace:
                        log.error('')
                        log.error('STACK TRACE:')
                    for line in method.result.stack_trace.splitlines():
                        log.error(f'  - {line}')
                    continue

                log.info(f'- ✅ [PASS] {fqn} ({method.result.duration.total_seconds():0.2f}s)')
                num_passed += 1

        if num_seen == 0:
            log.error('❌ No integration test output was detected.')
            return False
        
        if num_passed != num_seen:
            log.error(f'❌ Ran {num_seen} tests; {num_seen - num_passed} failed.')
            return False
        
        if self.exit_result != 'OK':
            log.error(f'❌ All tests passed, but final exit result was: {self.exit_result}')
            return False
        
        log.info('✅ All tests passed.')
        return True
            

    def read(self, line: str):
        match = self.regex.search(line)
        if not match:
            return
        prefix, message = match.group(1), match.group(2)
        prefix_tokens = prefix.split(':')
        head, tail = prefix_tokens[0], prefix_tokens[1:]

        if head == 'ANNOUNCE':
            if len(tail) > 0:
                type_index = int(tail[0])
                if len(tail) == 1:
                    type_full_name = message
                    assert len(self.types) == type_index
                    self.types.append(IntegrationTestType(type_full_name))
                else:
                    method_index = int(tail[1])
                    method_name = message
                    assert type_index < len(self.types)
                    assert len(self.types[type_index].methods) == method_index
                    self.types[type_index].methods.append(IntegrationTestMethod(method_name))
        elif head == 'INVOKE':
            method = self._find_method(tail)
            assert not method.invoked
            method.invoked = True
        elif head == 'RESULT':
            method = self._find_method(tail)
            if len(tail) <= 2:
                assert not method.result
                result_match = re.match(r'(PASSED|FAILED) after (\d+\.\d+)s', message)
                assert result_match
                passed = result_match.group(1) == 'PASSED'
                duration = timedelta(seconds=float(result_match.group(2)))
                method.result = IntegrationTestResult(passed, duration)
            else:
                assert method.result
                detail_type, detail_line_index = tail[2], int(tail[3])
                if detail_type == 'ERROR':
                    assert method.result.error_message.count('\n') == detail_line_index
                    method.result.error_message += message + '\n'
                else:
                    assert detail_type == 'STACK'
                    assert method.result.stack_trace.count('\n') == detail_line_index
                    method.result.stack_trace += message + '\n'
        elif head == 'EXIT':
            assert not self.exit_result
            self.exit_result = message
        elif head == 'ERROR':
            self.last_error = message
    
    def _find_method(self, tail: List[str]) -> IntegrationTestMethod:
        assert len(tail) >= 2
        type_index = int(tail[0])
        method_index = int(tail[1])
        assert type_index < len(self.types)
        assert method_index < len(self.types[type_index].methods)
        return self.types[type_index].methods[method_index]


def integration_test(project_path: str, platform: UnityBuildPlatform, target: UnityTarget, skip_build: bool):
    log = init_logger()

    # Resolve the target Unity project
    project = UnityProject.resolve(project_path)

    # Prepare to run a mock server that will record incoming HTTP requests from the SDK
    # and allow the integration tests to inspect those requests
    prepare_mock_server_venv()
    mock_server_addr = get_reachable_inet_addr()
    if not mock_server_addr:
        raise RuntimeError('Failed to resolve private IPv4 address for mock server')
    mock_server_port = 5100
    custom_endpoint_url = f'http://{mock_server_addr}:{mock_server_port}'

    # Generate a new installable app build that's configured to run integration tests
    config = UnityBuildConfig.DEVELOPMENT
    backend = DatadogBackendType.MOCK
    if skip_build:
        build = UnityBuild.resolve_existing(project, platform, config, target)
        log.info(f'Found existing build of {build.app_bundle_id}')
        log.info(f'- {build.app_bundle_path}')
        log.warning('This build is not guaranteed to be up to date or to match the build configuration with which you invoked this command.')
        log.warning('Use --skip-build only for quick iteration when no changes have been made to the project since the last build.')
    else:
        dd_env = 'integration-test'
        client_id, rum_application_id = '', '' # Use default fake values
        is_for_integration_test = True
        build = UnityBuild.generate(project, platform, config, target, backend, dd_env, custom_endpoint_url, client_id, rum_application_id, is_for_integration_test)
        log.info(f'Completed new build of {build.app_bundle_id}')
        log.info(f'- {build.app_bundle_path}')

    # Unity mucks with the adb server during builds: if we just ran an Android build,
    # restart the adb server so our adb commands will work reliably
    if platform == UnityBuildPlatform.ANDROID and not skip_build:
        Adb.require().restart_server()

    # Record the state of our integration tests as we parse lines of output
    handler = IntegrationTestRunnerOutputHandler()
    def _handle_output(line: str, _):
        handler.read(line)

    # Start up our mock server and acquire a device to run on
    with run_mock_server(mock_server_addr, mock_server_port), acquire_device(platform.value, target == UnityTarget.SIMULATOR) as device:
        log.info(f'Running on  device {device.device_id}')

        log.info(f'Uninstalling any existing versions of {build.app_bundle_id}...')
        device.uninstall_app(build.app_bundle_id)
        
        log.info(f'Installing new build from {build.app_bundle_path}...')
        device.install_app(build.app_bundle_path)

        if platform == UnityBuildPlatform.ANDROID and target == UnityTarget.SIMULATOR:
            time.sleep(2.0)

        device.tail_logs(_handle_output)
        device.launch_app(build.app_bundle_id)

        timeout_seconds = 5 * 60.0
        deadline = time.time() + timeout_seconds
        log.info(f'Integration tests running... (timeout {timeout_seconds}s; Ctrl-C to cancel)')
        while True:
            try:
                if handler.should_exit:
                    log.info('Output indicates that integration tests are finished; exiting...')
                    break
                if time.time() > deadline:
                    log.warning('Timeout exceeded; aborting integration tests.')
                    break
                time.sleep(0.1)
            except KeyboardInterrupt:
                log.info('')
                log.warning('Tests canceled.')
                break

    ok = handler.report(log)
    if not ok:
        sys.exit(1)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Runs all tests in Datadog.Unity.Tests.Integration, in a packaged build installed to a device')
    parser.add_argument('--project', default=__default_project_root__, help=f"Path to the root directory of the Unity project to run integration tests in; defaults to '{__default_project_name__}'")
    parser.add_argument('--platform', type=UnityBuildPlatform, choices=list(UnityBuildPlatform), required=True, help='The platform to test')
    parser.add_argument('--target', type=UnityTarget, choices=list(UnityTarget), default=UnityTarget.SIMULATOR , help='Whether to run on a simulator/AVD (default; will be started automatically) or an actual device (must be connected)')
    parser.add_argument('--skip-build', action='store_true', help="If true, no Unity build will be performed, and the script will use the latest build from '<project-path>/Build/<platform>': this should only be used for quick iteration when you know that no changes have been made since the last build")
    args = parser.parse_args()

    integration_test(args.project, args.platform, args.target, args.skip_build)
