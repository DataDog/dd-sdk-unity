#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import argparse
import asyncio
import os
from ios_helpers import xcodebuild
from unity_helpers import run_unity_command

demo_project_path = "../../samples/Demo Data"

def modify_datadog_settings(token, application_id):
    settings_file_name = "DatadogSettings.asset"
    settings_file_dir = os.path.join(demo_project_path, 'Assets', 'Resources')

    with open(os.path.join(settings_file_dir, settings_file_name)) as settings_file:
        data = settings_file.readlines()

    for i, line in enumerate(data):
        if line.startswith("  ClientToken:"):
            data[i] = f"  ClientToken: {token}\n"
        if line.startswith("  RumApplicationId:"):
            data[i] = f"  RumApplicationId: {application_id}\n"

    with open(os.path.join(settings_file_dir, settings_file_name), 'w') as settings_file:
        settings_file.writelines(data)


async def main():
    arg_parser = argparse.ArgumentParser()
    arg_parser.add_argument("--platform", choices=['ios', 'android'], help="The platform to export")
    arg_parser.add_argument("--retry", default=0, help="The number of times to retry if a Unity License cannot be obtained")
    arg_parser.add_argument("--retry-wait", default=100, help="The amount of time to wait before retrying after a license failure")
    arg_parser.add_argument("--client-token", help="The client token for the packaged application")
    arg_parser.add_argument("--application-id", help="The RUM Application Id for the packaged application")
    args = arg_parser.parse_args()

    if args.platform is None:
        print('--platform is required')
        return
    if args.client_token is None:
        print('--client-token is required')
        return
    if args.application_id is None:
        print('--application-id is required')
        return

    modify_datadog_settings(args.client_token, args.application_id)

    license_retry_count = args.retry
    license_retry_wait = args.retry_wait

    if args.platform == 'ios':
        await run_unity_command(license_retry_count, license_retry_wait,
            "-projectPath", f'"{demo_project_path}"', "-batchMode",
            "-executeMethod", "BuildCommands.BuildIOS",
            "-quit", "-logFile", "-",
        )

        build_path = os.path.join(demo_project_path, 'Build', 'iOS')
        xcodebuild(build_path, ['-workspace', 'Unity-iPhone.xcworkspace', '-scheme', 'Unity-iPhone', '-destination',
                    'generic/platform=iOS', '-archivePath', './Unity-iPhone.xcarchive', 'archive'])
        xcodebuild(build_path, ['-exportArchive', '-archivePath', './Unity-iPhone.xcarchive', '-exportPath', './export',
                    '-exportOptionsPlist', '../../exportOptions.plist'])
    elif args.platform == 'android':
        pass

if __name__ == "__main__":
    task = main()
    res = asyncio.get_event_loop().run_until_complete(task)
