#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import argparse
import asyncio
import os
from unity_helpers import get_unity_license, return_unity_license, run_unity_command

integration_project_path = "../../samples/Datadog Sample"

async def main():
    arg_parser = argparse.ArgumentParser()
    arg_parser.add_argument("--retry", default=0, help="The number of times to retry if a Unity License cannot be obtained")
    arg_parser.add_argument("--retry-wait", default=100, help="The amount of time to wait before retrying after a license failure")
    args = arg_parser.parse_args()

    license_retry_count = args.retry
    license_retry_wait = args.retry_wait

    is_ci = "IS_ON_CI" in os.environ
    token = None
    if is_ci:
        token = await get_unity_license()
        if token is None:
            print("Failed to get floatling license on CI")
            return 1


    await run_unity_command(license_retry_count, license_retry_wait,
        "-runTests", "-batchMode", "-projectPath", f'"{integration_project_path}"',
        "-testCategory", "!integration",
        "-testResults", "tmp/results.xml", "-logFile", "-",
    )

    if token is not None:
        await return_unity_license(token)

if __name__ == "__main__":
    task = main()
    res = asyncio.get_event_loop().run_until_complete(task)
    exit(res)
