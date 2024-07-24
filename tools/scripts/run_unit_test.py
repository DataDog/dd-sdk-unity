#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import argparse
from unity_helpers import run_unity_command

integration_project_path = "../../samples/Datadog Sample"

def main():
    arg_parser = argparse.ArgumentParser()
    arg_parser.add_argument("--retry", default=0, help="The number of times to retry if a Unity License cannot be obtained")
    arg_parser.add_argument("--retry-wait", default=100, help="The amount of time to wait before retrying after a license failure")
    args = arg_parser.parse_args()

    license_retry_count = args.retry
    license_retry_wait = args.retry_wait

    run_unity_command(license_retry_count, license_retry_wait,
        "-runTests", "-batchMode", "-projectPath", integration_project_path,
        "-testCategory", "!integration",
        "-testResults", "tmp/results.xml", "-logFile", "-",
    )

if __name__ == "__main__":
    main()
