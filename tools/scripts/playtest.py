"""
Builds a Unity project for iOS or Android, then runs it for end-to-end testing.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import time
import argparse
from contextlib import contextmanager
from typing import Optional, Tuple, Generator

from common.log import init_logger
from common.unity import UnityProject, UnityBuild, UnityBuildPlatform, UnityBuildConfig, UnityTarget, DatadogBackendType
from common.inet_addr import get_reachable_inet_addr
from common.mockserver import run_mock_server, prepare_mock_server_venv
from common.launch import LaunchConfig, launch_build
from common.android import Adb


@contextmanager
def _conditional_mock_server(mock_server_config: Optional[Tuple[str, int]]) -> Generator[None, None, None]:
    if not mock_server_config:
        yield
        return

    addr, port = mock_server_config
    prepare_mock_server_venv()
    with run_mock_server(addr, port):
        yield


def playtest(project_path: str, platform: UnityBuildPlatform, config: UnityBuildConfig, target: UnityTarget, backend: DatadogBackendType, backend_url: str, client_id: str, rum_application_id: str, is_for_demo: bool, skip_build: bool, skip_play: bool):
    log = init_logger()

    # Resolve the target Unity project
    project = UnityProject.resolve(project_path)

    # If backend is 'mock', we'll run a mock server locally and configure the Datadog
    # Unity SDK to send requests to it; for 'custom' we'll just configure the build
    # with the supplied custom endpoint URL; for 'live' we'll ensure the build has no
    # custom endpoint configured
    custom_endpoint_url = ''
    run_mock_server_on: Optional[Tuple[str, int]] = None
    if backend == DatadogBackendType.MOCK:
        mock_server_addr = get_reachable_inet_addr()
        if not mock_server_addr:
            raise RuntimeError('Failed to resolve private IPv4 address for mock server')
        mock_server_port = 5200
        custom_endpoint_url = f'http://{mock_server_addr}:{mock_server_port}'
        run_mock_server_on = (mock_server_addr, mock_server_port)
    elif backend == DatadogBackendType.CUSTOM:
        assert backend_url, 'backend_url must be specified when backend is custom'
        custom_endpoint_url = custom_endpoint_url
    else:
        assert backend == DatadogBackendType.LIVE
        assert not custom_endpoint_url

    if skip_build:
        build = UnityBuild.resolve_existing(project, platform, config, target)
        log.info(f'Found existing build of {build.app_bundle_id}')
        log.info(f'- {build.app_bundle_path}')
        log.warning('This build is not guaranteed to be up to date or to match the build configuration with which you invoked this command.')
        log.warning('Use --skip-build only for quick iteration when no changes have been made to the project since the last build.')
    else:
        dd_env = 'prod' if is_for_demo else 'playtest'
        is_for_integration_test = False
        build = UnityBuild.generate(project, platform, config, target, backend, dd_env, custom_endpoint_url, client_id, rum_application_id, is_for_integration_test)
        log.info(f'Completed new build of {build.app_bundle_id}')
        log.info(f'- {build.app_bundle_path}')

    # Unity mucks with the adb server during builds: if we just ran an Android build,
    # restart the adb server so our adb commands will work reliably
    if platform == UnityBuildPlatform.ANDROID and not skip_build:
        Adb.require().restart_server()

    if skip_play:
        log.info('Called with --skip-play; build is ready.')
        return

    # Prepare our runtime environment as configured: start up mock server if configured
    # to use one, start simulators if desired, etc.; then install and run the app
    with _conditional_mock_server(run_mock_server_on):
        use_simulator = target == UnityTarget.SIMULATOR
        with launch_build(platform, LaunchConfig(build, use_simulator, None)):
            log.info('App is running; press Ctrl-C when done.')
            while True:
                try:
                    time.sleep(0)
                except KeyboardInterrupt:
                    log.info('')
                    break
            log.info('Got Ctrl-C! Shutting down...')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Creates and runs a packaged build of a Unity project for iOS or Android, with Datadog SDK functionality')
    parser.add_argument('--project', required=True, help="Path to the root directory of the Unity project to build and run")
    parser.add_argument('--platform', type=UnityBuildPlatform, choices=list(UnityBuildPlatform), required=True, help='The platform to build an app bundle for')
    parser.add_argument('--config', type=UnityBuildConfig, choices=list(UnityBuildConfig), default=UnityBuildConfig.DEVELOPMENT, help='Whether to build the project in development/debug mode (default) or release/shipping mode; controls both Unity and native settings')
    parser.add_argument('--target', type=UnityTarget, choices=list(UnityTarget), default=UnityTarget.SIMULATOR, help='Whether to run on a simulator/AVD (default; will be started automatically) or an actual device (must be connected)')
    parser.add_argument('--backend', type=DatadogBackendType, choices=list(DatadogBackendType), default=DatadogBackendType.MOCK, help='Whether to run a transient mock server (default), configure a different custom intake endpoint, or send data to Datadog')
    parser.add_argument('--backend-url', help='URL to use as CustomEndpoint; required with --backend mock')
    parser.add_argument('--client-id', help="Client ID for Datadog API usage; required with --backend live; will default to 'fake-client-id' when using a backend other than live")
    parser.add_argument('--rum-application-id', help="ID of the RUM application in the configured Datadog org; required with --backend live; will default to 'fake-rum-application-id' when using a backend other than live")
    parser.add_argument('--demo', action='store_true', help='If true, configures the build to produce demo data')
    parser.add_argument('--skip-build', action='store_true', help="If true, no Unity build will be performed, and the script will use the latest build from '<project-path>/Build/<platform>': this should only be used for quick iteration when you know that no changes have been made since the last build")
    parser.add_argument('--skip-play', action='store_true', help='If true, the build will be written but not run (TODO: refactor commands/options)')
    args = parser.parse_args()

    if args.backend == DatadogBackendType.CUSTOM and not args.backend_url:
        parser.error("--backend-url is required when --backend is 'custom'")
    elif args.backend != DatadogBackendType.CUSTOM and args.backend_url:
        parser.error("--backend-url is not used unless --backend is 'custom'")

    if args.demo and args.backend != DatadogBackendType.LIVE:
        parser.error("--backend must be 'live' when building with --demo")

    if args.backend == DatadogBackendType.LIVE:
        if not args.client_id:
            parser.error("--client-id is required when --backend is 'live'")
        if not args.rum_application_id:
            # We could technically use the SDK without RUM enabled, but all of our
            # tests assume RUM is enabled, so we'll stick with that assumption for now
            parser.error("--rum-application-id is required when --backend is 'live'")

    playtest(
        project_path=args.project,
        platform=args.platform,
        config=args.config,
        target=args.target,
        backend=args.backend,
        backend_url=args.backend_url,
        client_id=args.client_id,
        rum_application_id=args.rum_application_id,
        is_for_demo=args.demo,
        skip_build=args.skip_build,
        skip_play=args.skip_play,
    )
