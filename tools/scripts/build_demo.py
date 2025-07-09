import os
import sys
import argparse
import shutil

from common.log import init_logger
from common.unity import UnityHub, UnityLicenseStatus, resolve_unity_install
from common.ddconfig import DatadogRuntimeConfig, modified_datadog_settings
from common.apple import run_xcodebuild


__repo_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

__default_demo_project_root__ = os.path.join(__repo_root__, 'samples', 'Demo Data')
__default_demo_project_unity_version__ = '2022'

__ios_output_relpath__ = os.path.join('Build', 'iOS')
__android_output_relpath__ = os.path.join('Build')


def build_demo(unity_version_prefix: str, project_root: str, platform: str, config: DatadogRuntimeConfig):
    log = init_logger()

    # Check to see if we have the required Unity version installed
    unity_hub = UnityHub.require()
    unity_installs = unity_hub.list_installs()
    unity_install = resolve_unity_install(unity_installs, unity_version_prefix)
    if not unity_install:
        raise RuntimeError(f'No Unity version matching {unity_version_prefix} is installed')

    # The target Unity project must have a BuildCommands.cs file that follows our 
    # internal conventions; sanity-check that it exists
    build_commands_cs_path = os.path.join(project_root, 'Assets', 'Editor', 'BuildCommands.cs')
    if not os.path.isfile(build_commands_cs_path):
        raise RuntimeError(f'Invalid Unity project: no BuildCommands script at {build_commands_cs_path}')
    
    # Temporarily modify the project's DatadogSettings asset to adopt our desired config
    with modified_datadog_settings(project_root, config):
        # Run the Unity build: for iOS this generates an Xcode project; for other
        # platforms it generates the final packaged build
        build_command = 'BuildCommands.BuildHeadless'
        build_command_args = ['-buildPlatform', platform]
        result = unity_install.run_batchmode(project_root, '-quit', '-executeMethod', build_command, *build_command_args)
        if result.exitcode == 0:
            log.info('Unity build finished successfully.')
        elif result.license_status != UnityLicenseStatus.VALID:
            log.error('Unity failed to acquire a license.')
            return 86
        else:
            raise RuntimeError(f'Unity build exited with status code {result.exitcode}')
    
    # On Android, Unity should have written an .apk, in which case we're done
    if platform == 'android':
        apk_path = os.path.join(project_root, 'Build', 'Android', 'datadog-demo.apk')
        if not os.path.isfile(apk_path):
            raise RuntimeError(f'APK not found after successful Android build: {apk_path}')
        log.info(apk_path)
        return 0
    
    # On iOS, Unity just generates an Xcode project, so we need to invoke an Xcode
    # build to generate our final iOS app
    if platform == 'ios':
        ios_build_dir = os.path.join(project_root, 'Build', 'iOS')
        if not os.path.isdir(ios_build_dir):
            raise RuntimeError(f'Xcode project not found after successful iOS build: {ios_build_dir}')

        # Ensure that we're working from a clean Xcode project; clear any existing artifacts
        ios_derived_data_dir = os.path.join(ios_build_dir, 'build')
        if os.path.isdir(ios_derived_data_dir):
            log.info(f'Removing existing derived data directory: {ios_derived_data_dir}')
            shutil.rmtree(ios_derived_data_dir)

        ios_export_dir = os.path.join(ios_build_dir, 'export')
        if os.path.isdir(ios_export_dir):
            log.info(f'Removing existing export directory: {ios_export_dir}')
            shutil.rmtree(ios_export_dir)

        # Run Xcode's 'archive' command to build the project for iOS, placing the build
        # artifacts (i.e. UnityDemoApp.app) in a 'build' subdirectory, then packaging that build
        # into an .xcarchive file
        run_xcodebuild(ios_build_dir, [
            '-derivedDataPath', './build',
            '-workspace', 'Unity-iPhone.xcworkspace',
            '-scheme', 'Unity-iPhone',
            '-destination', 'generic/platform=iOS',
            '-archivePath', './Unity-iPhone.xcarchive', 
            'archive',
        ])

        # Bundle a final .ipa file from that .xcarchive, placing it in 'export'
        run_xcodebuild(ios_build_dir, [
            '-exportArchive',
            '-archivePath', './Unity-iPhone.xcarchive',
            '-exportPath', './export',
            '-exportOptionsPlist', '../../exportOptions.plist',
        ])

        # Verify that we have an .ipa archive in the expected location
        ipa_path = os.path.join(ios_export_dir, 'UnityDemoApp.ipa')
        if not os.path.isfile(ipa_path):
            raise RuntimeError(f'IPA not found after successful iOS build: {ipa_path}')
        log.info(ipa_path)
        return 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Build the Demo Data project, configured appropriately')
    parser.add_argument('--unity-version', '-u', default=__default_demo_project_unity_version__, help='The target version of Unity to build with; may be a partial specifier (e.g. "6000", "2023.3")')
    parser.add_argument('--project', '-p', default=__default_demo_project_root__, help="Path to the root directory of the Unity project to load; defaults to 'samples/Demo Data' in this repo")
    parser.add_argument('--platform', choices=['ios', 'android'], required=True, help='The platform to build an app bundle for')
    parser.add_argument('--custom-endpoint', type=str, help='The URL for a custom intake endpoint to be used during tests')
    parser.add_argument('--client-token', required=True, help='The Datadog client token to use in the packaged application')
    parser.add_argument('--application-id', required=True, help='The RUM Application ID for the packaged application')
    args = parser.parse_args()

    config = DatadogRuntimeConfig(
        custom_endpoint=args.custom_endpoint,
        client_token=args.client_token,
        rum_application_id=args.application_id,
    )

    sys.exit(build_demo(args.unity_version, args.project, args.platform, config))
