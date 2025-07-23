"""
Builds a Unity project for iOS or Android, then runs it for end-to-end testing.

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
from platform import machine
from dataclasses import dataclass, field
from contextlib import contextmanager
from typing import List, Optional, Tuple, Generator

from common.log import init_logger
from common.unity import UnityProject
from common.inet_addr import get_reachable_inet_addr
from common.mockserver import run_mock_server, prepare_mock_server_venv
from common.device import acquire_device, __default_ios_device__, TargetDevice
from common.android import get_package_name, Adb
from common.apple import run_xcodebuild, get_bundle_identifier
from common.shell import OutputHandlerFunc


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



@contextmanager
def _prepare_for_run(mock_server_config: Optional[Tuple[str, int]], platform: str, target: str) -> Generator[TargetDevice, None, None]:

    @contextmanager
    def _conditional_mock_server():
        if not mock_server_config:
            yield
            return
        
        addr, port = mock_server_config
        prepare_mock_server_venv()
        with run_mock_server(addr, port):
            yield

    with _conditional_mock_server(), acquire_device(platform, target == 'simulator') as device:
        yield device


def _resolve_android_build(project: UnityProject) -> Tuple[str, str]:
    # Find the .apk that should have been written during the Unity build
    apk_path = os.path.join(project.path, 'Build', 'Android', 'datadog-demo.apk')
    if not os.path.isfile(apk_path):
        raise RuntimeError(f'APK not found for Android build: {apk_path}')

    # Use aapt to parse the Android package name
    app_bundle_path = apk_path
    app_bundle_id = get_package_name(apk_path)

    return app_bundle_path, app_bundle_id


def _get_xcode_config(config: str) -> str:
    return {'development':   'Debug', 'release': 'Release'}[config]


def _resolve_ios_build(project: UnityProject, config: str, target: str) -> Tuple[str, str]:
    # Locate the root directory of the Xcode project generated by Unity
    ios_build_dir = os.path.join(project.path, 'Build', 'iOS')
    if not os.path.isdir(ios_build_dir):
        raise RuntimeError(f'Xcode project not found after successful iOS build: {ios_build_dir}')

    # Find the directory containing output files from the Xcode build
    xcode_config = _get_xcode_config(config)
    artifact_dir_name = f'{xcode_config}-iphone{"simulator" if target == "simulator" else "os"}'
    artifact_dir = os.path.join(ios_build_dir, 'DerivedData', 'Build', 'Products', artifact_dir_name)
    if not os.path.isdir(artifact_dir):
        raise RuntimeError(f'Failed to find artifacts of successful Xcode build: {artifact_dir}')
    
    # Identify the app bundle: it should be the only directory with an '.app' extension
    candidate_dirnames = [f for f in os.listdir(artifact_dir) if f.endswith('.app')]
    if not candidate_dirnames:
        raise RuntimeError(f'Failed to find .app directory in Xcode build artifacts: {artifact_dir}')
    if len(candidate_dirnames) != 1:
        raise RuntimeError(f'Found multiple .app directories in Xcode build artifacts: {artifact_dir}')
    app_bundle_path = os.path.join(artifact_dir, candidate_dirnames[0])
    
    # Get the bundle identifier for our iOS app
    plist_path = os.path.join(app_bundle_path, 'Info.plist')
    if not os.path.isfile(plist_path):
        raise RuntimeError(f'Info.plist not found in iOS app build: {plist_path}')

    # Use plutil to parse the iOS bundle identifier
    app_bundle_id = get_bundle_identifier(plist_path)
    return app_bundle_path, app_bundle_id


def _resolve_existing_build(project: UnityProject, platform: str, config: str, target: str) -> Tuple[str, str]:
    if platform == 'android':
        return _resolve_android_build(project)
    else:
        assert platform == 'ios'
        return _resolve_ios_build(project, config, target)


def _generate_build(project: UnityProject, platform: str, config: str, target: str, backend: str, custom_endpoint_url: str, client_id: str, rum_application_id: str, mode: str) -> Tuple[str, str]:
    # Prepare to inject some temporary scripts into the project during the build: this
    # allows us to target any project with a 'DatadogBuild.yml' file, so that Unity C#
    # scripts related to building and testing the SDK don't need to be managed
    # separately for each project
    script_paths: List[str] = []
    script_paths.append('Assets/Editor/DatadogBuild/BuildCommands.cs')

    # If we're using a custom intake server that's running on an insecure endpoint
    # (i.e. we're using 'http:' rather than 'https:' because we're running locally),
    # configure the build to allow non-TLS HTTP traffic
    if backend != 'live' and custom_endpoint_url.startswith('http:'):
        if platform == 'android':
            script_paths.append('Assets/Editor/DatadogBuild/EnableCleartextTrafficPostProcessor.cs')

    # If we're generating a build to run integration tests, we'll include a custom
    # MonoBehaviour that can bootstrap and run our integration tests
    if mode == 'integration-test':
        script_paths.append('Assets/DatadogBuildRuntimeScripts/IntegrationTestRunner.cs')
        if platform == 'ios':
            script_paths.append('Assets/Plugins/iOS/IntegrationTestLogger.m')
    
    # Prepare the arguments for our injected build script
    build_command = 'Datadog.Unity.Build.BuildCommands.BuildHeadless'
    build_args = [
        '-buildPlatform', platform,
        '-buildConfig', config,
    ]

    # Make sure the Datadog SDK is enabled and configured appropriately for our desired
    # runtime environment
    build_args += [
        '-datadogSettings:Enabled', 'true',
        '-datadogSettings:RumEnabled', 'true',
    ]

    # Tag the environment as 'prod' for demo data; 'integration-test' for integration
    # tests, and 'playtest' as a catch-all for throwaway development builds
    dd_env = 'playtest'
    if mode == 'integration-test':
        dd_env = 'integration-test'
    elif mode == 'demo':
        dd_env = 'prod'
    build_args += ['-datadogSettings:Env', dd_env]

    # Require user-supplied credentials if we're sending data to a live Datadog intake
    # endpoint; allow fake values if we're using a mock server or other custom endpoint
    if backend == 'live':
        assert client_id, '--client-id must be supplied when using live backend'
        assert rum_application_id, '--rum-application-id must be supplied when using live backend'
        build_args += [
            '-datadogSettings:ClientToken', client_id,
            '-datadogSettings:RumApplicationId', rum_application_id,
        ]
    else:
        build_args += [
            '-datadogSettings:ClientToken', client_id or 'fake-client-id',
            '-datadogSettings:RumApplicationId', rum_application_id or 'fake-rum-application-id',
        ]

    # Set the CustomEndpoint URL for non-live backends; clear it for live backend
    if backend == 'live':
        assert not custom_endpoint_url
        build_args += ['-datadogSettings:CustomEndpoint', 'CLEAR']
    else:
        assert custom_endpoint_url
        build_args += ['-datadogSettings:CustomEndpoint', custom_endpoint_url]

    # Apply some common-sense defaults to make the SDK use the full complement of
    # features and send data frequently.
    #
    # NOTE: These settings are _required_ for integration tests; if we update this code
    # to allow more flexible configuration in other modes, make sure we're still
    # supplying these values if mode == 'integration-test'
    build_args += [
        '-datadogSettings:SdkVerbosity', 'warn',
        '-datadogSettings:BatchSize', 'small',
        '-datadogSettings:UploadFrequency', 'frequent',
        '-datadogSettings:BatchProcessingLevel', 'medium',
        '-datadogSettings:CrashReportingEnabled', 'true',
        '-datadogSettings:ForwardUnityLogs', 'true',
        '-datadogSettings:RemoteLogThreshold', 'log',
        '-datadogSettings:AutomaticSceneTracking', 'true',
        '-datadogSettings:SessionSampleRate', '100',
        '-datadogSettings:TraceSampleRate', '100',
        '-datadogSettings:TraceContextInjection', 'all',
        '-datadogSettings:FirstPartyHosts', 'shopist.io,api.shopist.io',
        '-datadogSettings:TelemetrySampleRate', '100',
    ]

    # On iOS, the Xcode project needs to be generated differently depending on whether
    # we're targeting simulator or device, and that setting is stored in
    # ProjectSettings.asset
    if platform == 'ios':
        build_args += ['-projectSettings:iPhoneSdkVersion', target]
        if target == 'simulator' and machine().lower().startswith('arm'):
            build_args += ['-projectSettings:iOSSimulatorArchitecture', 'arm64']

    # If we're generating a build that will run integration tests, ensure that we're
    # including test assemblies, and configure the build to generate a transient blank
    # scene that will contain our integration test runtime script
    if mode == 'integration-test':
        build_args += [
            '-includeTestAssemblies',
            '-integrationTestSceneOnly',
            '-define:DD_RUNTIME_INTEGRATION_TESTS',
        ]

    # Inject scripts, then run the build in Unity, then remove the injected scripts
    app_bundle_path = ''
    app_bundle_id = ''
    with project.injected_scripts(script_paths):
        # Execute our build script, which uses BuildPipeline.BuildPlayer and which will
        # exit with status code 1 if it fails
        project.run('-executeMethod', build_command, *build_args)

        # For Android, Unity should have written an installable .apk
        if platform == 'android':
            app_bundle_path, app_bundle_id = _resolve_android_build(project)

        # For iOS, Unity just generates an Xcode project, so we need to invoke an Xcode
        # build to generate our installable iOS app bundle
        elif platform == 'ios':
            ios_build_dir = os.path.join(project.path, 'Build', 'iOS')
            if not os.path.isdir(ios_build_dir):
                raise RuntimeError(f'Xcode project not found after successful iOS build: {ios_build_dir}')
            
            # Unity's build configuration is independent of the build configuration for
            # the iOS app; we'll just control them both from the same option
            xcode_config = _get_xcode_config(config)

            # We need to pass different 'destination' args depending on whether we're
            # targeting simulator or device
            destination = 'generic/platform=iOS'
            if target == 'simulator':
                destination = __default_ios_device__.xcode_destination

            # Invoke xcodebuild to generate an iOS app bundle
            run_xcodebuild(ios_build_dir, [
                '-workspace', 'Unity-iPhone.xcworkspace',
                '-scheme', 'Unity-iPhone',
                '-configuration', xcode_config,
                '-destination', destination,
                '-derivedDataPath', './DerivedData',
            ])
            app_bundle_path, app_bundle_id = _resolve_ios_build(project, config, target)

        # Build scripts support iOS and Android only
        else:
            raise RuntimeError(f'Unsupported build platform {platform}')
        
    # Build complete: we should have an installable app bundle and we should know what
    # it's called
    assert app_bundle_path
    assert app_bundle_id
    return app_bundle_path, app_bundle_id


def playtest(project_path: str, platform: str, config: str, target: str, backend: str, backend_url: str, client_id: str, rum_application_id: str, mode: str, skip_build: bool, skip_play: bool):
    log = init_logger()

    # Validate args
    assert config in ['development', 'release'], f'Invalid config: {config}'
    assert target in ['simulator', 'device'], f'Invalid target: {target}'
    assert backend in ['mock', 'custom', 'live'], f'Invalid backend: {backend}'
    assert mode in ['interactive', 'smoke', 'integration-test', 'demo'], f'Invalid mode: {mode}'

    # Resolve the target Unity project
    project = UnityProject.resolve(project_path)

    # If backend is 'mock', we'll run a mock server locally and configure the Datadog
    # Unity SDK to send requests to it; for 'custom' we'll just configure the build
    # with the supplied custom endpoint URL; for 'live' we'll ensure the build has no
    # custom endpoint configured
    custom_endpoint_url = ''
    run_mock_server_on: Optional[Tuple[str, int]] = None
    if backend == 'mock':
        mock_server_addr = get_reachable_inet_addr()
        if not mock_server_addr:
            raise RuntimeError('Failed to resolve private IPv4 address for mock server')
        mock_server_port = 5200
        custom_endpoint_url = f'http://{mock_server_addr}:{mock_server_port}'
        run_mock_server_on = (mock_server_addr, mock_server_port)
    elif backend == 'custom':
        assert backend_url, 'backend_url must be specified when backend is custom'
        custom_endpoint_url = custom_endpoint_url
    else:
        assert backend == 'live'
        assert not custom_endpoint_url

    if skip_build:
        app_bundle_path, app_bundle_id = _resolve_existing_build(project, platform, config, target)
        log.info(f'Found existing build of {app_bundle_id}')
        log.info(f'- {app_bundle_path}')
        log.warning('This build is not guaranteed to be up to date or to match your current settings for --config/--target/etc')
        log.warning('Use --skip-build only for quick iteration when no changes have been made to the project since the last build.')
    else:
        app_bundle_path, app_bundle_id = _generate_build(project, platform, config, target, backend, custom_endpoint_url, client_id, rum_application_id, mode)
        log.info(f'Completed new build of {app_bundle_id}')
        log.info(f'- {app_bundle_path}')

    # Unity mucks with the adb server during builds: if we just ran an Android build,
    # restart the adb server so our adb commands will work reliably
    if platform == 'android' and not skip_build:
        Adb.require().restart_server()

    if skip_play:
        log.info('Called with --skip-play; build is ready.')
        return 0

    # Prepare our runtime environment as configured: start up mock server if configured
    # to use one, start simulators if desired, etc.; then install and run the app
    with _prepare_for_run(run_mock_server_on, platform, target) as device:
        log.info(f'Running on  device {device.device_id}')

        log.info(f'Uninstalling any existing versions of {app_bundle_id}...')
        device.uninstall_app(app_bundle_id)
        
        log.info(f'Installing new build from {app_bundle_path}...')
        device.install_app(app_bundle_path)

        if platform == 'android' and target == 'simulator':
            time.sleep(2.0)

        integration_test_handler: Optional[IntegrationTestRunnerOutputHandler] = None
        log_handler: Optional[OutputHandlerFunc] = None
        if mode == 'integration-test':
            integration_test_handler = IntegrationTestRunnerOutputHandler()
            def _read(line: str, _):
                integration_test_handler.read(line)
            log_handler = _read

        log.info(f'Launching {app_bundle_id}...')
        device.tail_logs(log_handler)
        device.launch_app(app_bundle_id)

        if mode in ('interactive', 'demo'):
            log.info('App is running; press Ctrl-C when done.')
            while True:
                try:
                    time.sleep(0)
                except KeyboardInterrupt:
                    log.info('')
                    break
            log.info('Got Ctrl-C! Shutting down...')

        elif mode == 'smoke':
            # TODO: Detect crashes/errors
            smoke_timeout_seconds = 10.0
            log.info(f'App is running; we will exit in {smoke_timeout_seconds} seconds...')
            time.sleep(smoke_timeout_seconds)
            log.info('Smoke test concluded! Shutting down...')

        elif mode == 'integration-test':
            integration_test_timeout_seconds = 120.0
            deadline = time.time() + integration_test_timeout_seconds
            log.info(f'Integration tests running... (timeout {integration_test_timeout_seconds}s; Ctrl-C to cancel)')
            while True:
                try:
                    assert integration_test_handler
                    if integration_test_handler.should_exit:
                        break
                    if time.time() > deadline:
                        log.warning('Timeout exceeded; aborting integration tests.')
                        break
                    time.sleep(0.1)
                except KeyboardInterrupt:
                    log.info('')
                    log.warning('Tests canceled.')
                    break

    if mode == 'integration-test':
        assert integration_test_handler
        ok = integration_test_handler.report(log)
        if not ok:
            return 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Creates and runs a packaged build of a Unity project for iOS or Android, with Datadog SDK functionality')
    parser.add_argument('--project', '-p', required=True, help="Path to the root directory of the Unity project to build and run")
    parser.add_argument('--platform', choices=['ios', 'android'], required=True, help='The platform to build an app bundle for')
    parser.add_argument('--config', choices=['development', 'release'], default='development', help='Whether to build the project in development/debug mode (default) or release/shipping mode; controls both Unity and native settings')
    parser.add_argument('--target', choices=['simulator', 'device'], default='simulator' , help='Whether to run on a simulator/AVD (default; will be started automatically) or an actual device (must be connected)')
    parser.add_argument('--backend', choices=['mock', 'custom', 'live'], default='mock', help='Whether to run a transient mock server (default), configure a different custom intake endpoint, or send data to Datadog')
    parser.add_argument('--backend-url', help='URL to use as CustomEndpoint; required with --backend mock')
    parser.add_argument('--client-id', help="Client ID for Datadog API usage; required with --backend live; will default to 'fake-client-id' when using a backend other than live")
    parser.add_argument('--rum-application-id', help="ID of the RUM application in the configured Datadog org; required with --backend live; will default to 'fake-rum-application-id' when using a backend other than live")
    parser.add_argument('--mode', choices=['interactive', 'smoke', 'integration-test', 'demo'], default='interactive', help="If 'integration-test', the app will be built with test assemblies intact, and the resulting build will execute the integration tests and then exit. If 'smoke', the game will boot, run for 10 seconds, and exit. 'integration-test' will produce a build that runs integration tests and then exits. 'demo' will generate a build suitable for generating demo data (usually in conjunction with '--backend live' and credentials supplied via '--client-id', and '--rum-application-id')")
    parser.add_argument('--skip-build', action='store_true', help="If true, no Unity build will be performed, and the script will use the latest build from '<project-path>/Build/<platform>': this should only be used for quick iteration when you know that no changes have been made since the last build")
    parser.add_argument('--skip-play', action='store_true', help='If true, the build will be written but not run (TODO: refactor commands/options)')
    args = parser.parse_args()

    if args.backend == 'custom' and not args.backend_url:
        parser.error("--backend-url is required when --backend is 'custom'")
    elif args.backend != 'custom' and args.backend_url:
        parser.error("--backend-url is not used unless --backend is 'custom'")
    
    if args.backend == 'live':
        if not args.client_id:
            parser.error("--client-id is required when --backend is 'live'")
        if not args.rum_application_id:
            # We could technically use the SDK without RUM enabled, but all of our
            # tests assume RUM is enabled, so we'll stick with that assumption for now
            parser.error("--rum-application-id is required when --backend is 'live'")

    sys.exit(playtest(
        project_path=args.project,
        platform=args.platform,
        config=args.config,
        target=args.target,
        backend=args.backend,
        backend_url=args.backend_url,
        client_id=args.client_id,
        rum_application_id=args.rum_application_id,
        mode=args.mode,
        skip_build=args.skip_build,
        skip_play=args.skip_play,
    ))
