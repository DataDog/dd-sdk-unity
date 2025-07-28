"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
from platform import machine
from enum import Enum
from dataclasses import dataclass
from typing import List, Optional

from ..device import __default_ios_device__
from ..apple import run_xcodebuild, get_bundle_identifier
from ..android import get_package_name
from .project import UnityProject


class UnityBuildPlatform(str, Enum):
    IOS = 'ios'
    ANDROID = 'android'


class UnityBuildConfig(str, Enum):
    DEVELOPMENT = 'development'  # Debuggable build with Unity profiling and debugging tools
    RELEASE = 'release'          # Fully optimized production build w/o Unity debugging tools

    @property
    def xcode_config(self) -> str:
        if self == UnityBuildConfig.DEVELOPMENT:
            return 'Debug'
        if self == UnityBuildConfig.RELEASE:
            return 'Release'
        raise ValueError(f'Unexpected value for UnityBuildConfig')


class UnityTarget(str, Enum):
    SIMULATOR = 'simulator'  # Deploy to an emulated device running on the local machine
    DEVICE = 'device'        # Deploy to a physical device connected to the local machine


class DatadogBackendType(str, Enum):
    MOCK = 'mock'      # Run a mock server within the script, and configure it as custom endpoint
    CUSTOM = 'custom'  # Use a custom endpoint URL specified by the caller
    LIVE = 'live'      # No custom endpoint; send data to Datadog


@dataclass
class UnityBuild:
    app_bundle_path: str  # Path to .apk file or .app directory on disk
    app_bundle_id: str    # Package name, CFBundleIdentifier, etc.

    @classmethod
    def generate(cls, project: UnityProject, platform: UnityBuildPlatform, config: UnityBuildConfig, target: UnityTarget, backend: DatadogBackendType, dd_env: str, custom_endpoint_url: str, client_id: str, rum_application_id: str, is_for_integration_test: bool) -> 'UnityBuild':
        # Prepare to inject some temporary scripts into the project during the build: this
        # allows us to target any project with a 'DatadogBuild.yml' file, so that Unity C#
        # scripts related to building and testing the SDK don't need to be managed
        # separately for each project
        script_paths: List[str] = []
        script_paths.append('Assets/Editor/DatadogBuild/BuildCommands.cs')

        # If we're using a custom intake server that's running on an insecure endpoint
        # (i.e. we're using 'http:' rather than 'https:' because we're running locally),
        # configure the build to allow non-TLS HTTP traffic
        if backend != DatadogBackendType.LIVE and custom_endpoint_url.startswith('http:'):
            if platform == UnityBuildPlatform.ANDROID:
                script_paths.append('Assets/Editor/DatadogBuild/EnableCleartextTrafficPostProcessor.cs')

        # If we're generating a build to run integration tests, we'll include a custom
        # MonoBehaviour that can bootstrap and run our integration tests
        if is_for_integration_test:
            script_paths.append('Assets/DatadogBuildRuntimeScripts/IntegrationTestRunner.cs')
            if platform == UnityBuildPlatform.IOS:
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
        if dd_env:
            build_args += ['-datadogSettings:Env', dd_env]

        # Require user-supplied credentials if we're sending data to a live Datadog intake
        # endpoint; allow fake values if we're using a mock server or other custom endpoint
        if backend == DatadogBackendType.LIVE:
            assert client_id, 'client_id must be supplied when using live backend'
            assert rum_application_id, 'rum_application_id must be supplied when using live backend'
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
        if backend == DatadogBackendType.LIVE:
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
        # supplying these values if is_for_integration_test
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
        if platform == UnityBuildPlatform.IOS:
            build_args += ['-projectSettings:iPhoneSdkVersion', target]
            if target == UnityTarget.SIMULATOR and machine().lower().startswith('arm'):
                build_args += ['-projectSettings:iOSSimulatorArchitecture', 'arm64']

        # If we're generating a build that will run integration tests, ensure that we're
        # including test assemblies, and configure the build to generate a transient blank
        # scene that will contain our integration test runtime script
        if is_for_integration_test:
            build_args += [
                '-includeTestAssemblies',
                '-integrationTestSceneOnly',
                '-define:DD_RUNTIME_INTEGRATION_TESTS',
            ]

        # Inject scripts, then run the build in Unity, then remove the injected scripts
        build: Optional[UnityBuild] = None
        with project.injected_scripts(script_paths):
            # Execute our build script, which uses BuildPipeline.BuildPlayer and which will
            # exit with status code 1 if it fails
            project.run('-executeMethod', build_command, *build_args)

            # For Android, Unity should have written an installable .apk
            if platform == UnityBuildPlatform.ANDROID:
                build = cls._resolve_android(project)

            # For iOS, Unity just generates an Xcode project, so we need to invoke an Xcode
            # build to generate our installable iOS app bundle
            elif platform == UnityBuildPlatform.IOS:
                ios_build_dir = os.path.join(project.path, 'Build', 'iOS')
                if not os.path.isdir(ios_build_dir):
                    raise RuntimeError(f'Xcode project not found after successful iOS build: {ios_build_dir}')
                
                # Unity's build configuration is independent of the build configuration for
                # the iOS app; we'll just control them both from the same option
                xcode_config = config.xcode_config

                # We need to pass different 'destination' args depending on whether we're
                # targeting simulator or device
                destination = 'generic/platform=iOS'
                if target == UnityTarget.SIMULATOR:
                    destination = __default_ios_device__.xcode_destination

                # Invoke xcodebuild to generate an iOS app bundle
                run_xcodebuild(ios_build_dir, [
                    '-workspace', 'Unity-iPhone.xcworkspace',
                    '-scheme', 'Unity-iPhone',
                    '-configuration', xcode_config,
                    '-destination', destination,
                    '-derivedDataPath', './DerivedData',
                ])
                build = cls._resolve_ios(project, config, target)

            # Build scripts support iOS and Android only
            else:
                raise RuntimeError(f'Unsupported build platform {platform}')
            
        # Build complete: we should have an installable app bundle and we should know what
        # it's called
        assert build
        assert build.app_bundle_path
        assert build.app_bundle_id
        return build
    
    @classmethod
    def resolve_existing(cls, project: UnityProject, platform: UnityBuildPlatform, config: UnityBuildConfig, target: UnityTarget) -> 'UnityBuild':
        if platform == UnityBuildPlatform.ANDROID:
            return cls._resolve_android(project)
        else:
            assert platform == UnityBuildPlatform.IOS
            return cls._resolve_ios(project, config, target)

    @classmethod
    def _resolve_android(cls, project: UnityProject) -> 'UnityBuild':
        # Find the .apk that should have been written during the Unity build
        apk_path = os.path.join(project.path, 'Build', 'Android', 'datadog-demo.apk')
        if not os.path.isfile(apk_path):
            raise RuntimeError(f'APK not found for Android build: {apk_path}')

        # Use aapt to parse the Android package name
        app_bundle_path = apk_path
        app_bundle_id = get_package_name(apk_path)

        return cls(app_bundle_path, app_bundle_id)

    @classmethod
    def _resolve_ios(cls, project: UnityProject, config: UnityBuildConfig, target: UnityTarget) -> 'UnityBuild':
        # Locate the root directory of the Xcode project generated by Unity
        ios_build_dir = os.path.join(project.path, 'Build', 'iOS')
        if not os.path.isdir(ios_build_dir):
            raise RuntimeError(f'Xcode project not found after successful iOS build: {ios_build_dir}')

        # Find the directory containing output files from the Xcode build
        xcode_config = config.xcode_config
        artifact_dir_name = f'{xcode_config}-iphone{"simulator" if target == UnityTarget.SIMULATOR else "os"}'
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
        return cls(app_bundle_path, app_bundle_id)
