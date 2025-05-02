#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import argparse
import asyncio
import os
import unity_helpers as uh

default_project_path = "../../samples/Datadog Sample"

async def main():
    arg_parser = argparse.ArgumentParser()
    arg_parser.add_argument("--retry", default=0, help="The number of times to retry if a Unity License cannot be obtained")
    arg_parser.add_argument("--retry-wait", default=100, help="The amount of time to wait before retrying after a license failure")
    arg_parser.add_argument("--unity-version", default=uh.DEFAULT_UNITY_VERSION, help="What version of Unity to use. May be a partial version.")
    arg_parser.add_argument("--project-path", default=default_project_path, help="The path of the project to run unit tests on.")
    args = arg_parser.parse_args()

    license_retry_count = args.retry
    license_retry_wait = args.retry_wait

    await uh.get_full_unity_version(args.unity_version, update_global=True)

    print(f'Got Unity Version {uh.unity_version}')

    is_ci = "IS_ON_CI" in os.environ
    token = None
    if is_ci:
        token = await uh.get_unity_license()
        if token is None:
            print("Failed to get floatling license on CI")
            return 1

    project_path = args.project_path

    return_code = await uh.run_unity_command(license_retry_count, license_retry_wait,
        "-runTests", "-batchMode", "-projectPath", f'"{project_path}"',
        "-testCategory", "!integration",
        "-testResults", "tmp/results.xml", "-logFile", "-",
    )

    return_code = await uh.run_unity_command(license_retry_count, license_retry_wait,
        "-runTests", "-batchMode", "-projectPath", f'"{project_path}"',
        "-testCategory", "!integration", '-testPlatform', 'PlayMode',
        "-testResults", "tmp/results-play-mode.xml", "-logFile", "-",
    )

    if token is not None:
        await uh.return_unity_license(token)

    uh.transform_nunit_to_junit(f"{project_path}/tmp/results.xml", f"{project_path}/tmp/junit-results.xml")
    uh.transform_nunit_to_junit(f"{project_path}/tmp/results-play-mode.xml", f"{project_path}/tmp/junit-results-play-mode.xml")

    return return_code

if __name__ == "__main__":
    task = main()
    res = asyncio.get_event_loop().run_until_complete(task)
    exit(res)
