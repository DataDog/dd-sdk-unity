#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import argparse
import asyncio
import subprocess
import os
import threading

import ios_helpers
import android_helpers
from unity_helpers import run_unity_command

integration_project_path = "../../samples/Datadog Sample"

def run_mock_server():
    mock_server_dir = "../mock_server/"
    run_server_command = "./venv/bin/python3"
    return subprocess.Popen([run_server_command, "app.py"],
                          stdout=subprocess.PIPE,
                          stderr=subprocess.STDOUT,
                          cwd=mock_server_dir,
                          universal_newlines=True)

def output_reader(mock_server):
    for line in iter(mock_server.stdout.readline, ''):
        print(f'[mock_server] {line}', end='')

def modify_datadog_settings(local_server_address):
    settings_file_name = "DatadogSettings.asset"
    settings_file_dir = os.path.join(integration_project_path, 'Assets', 'Resources')

    with open(os.path.join(settings_file_dir, settings_file_name)) as settings_file:
        data = settings_file.readlines()

    for i, line in enumerate(data):
        if line.startswith("  CustomEndpoint:"):
            data[i] = f"  CustomEndpoint: {local_server_address}\n"

    with open(os.path.join(settings_file_dir, settings_file_name), 'w') as settings_file:
        settings_file.writelines(data)


async def main():
    arg_parser = argparse.ArgumentParser()
    arg_parser.add_argument("--platform", choices=['ios', 'android'], help="The platform to run integration tests on.")
    arg_parser.add_argument("--launch-simulator", action='store_true', help="Whether to launch a simulator or emulator before running tests.")
    arg_parser.add_argument("--retry", default=0, help="The number of times to retry if a Unity License cannot be obtained")
    arg_parser.add_argument("--retry-wait", default=100, help="The amount of time to wait before retrying after a license failure")
    args = arg_parser.parse_args()

    if args.platform is None:
        print('--platform is required')
        return

    if args.launch_simulator:
        if args.platform == 'ios':
            project_settings_path = os.path.join(integration_project_path, 'ProjectSettings', 'ProjectSettings.asset')
            ios_helpers.switch_to_simulator_target(project_settings_path)
            ios_helpers.launch_ios_simulator('iOS-17-4', 'iPhone 15')
        elif args.platform == 'android':
            android_helpers.launch_android_emulator("33", None)


    mock_server = run_mock_server()

    # Find the IP address we started on
    local_server_address = None
    for line in iter(mock_server.stdout.readline, ''):
        print(f'[mock_server] {line}', end='')
        if line.strip().startswith("* Running on"):
            local_server_address = line.strip().split(' ')[3]
            break

    if local_server_address is None:
        print("Could not find mock server address before server closed. Terminating.")
        exit(1)

    # Start a thread for the mock server output
    t = threading.Thread(target=output_reader, args=(mock_server,))
    t.start()

    modify_datadog_settings(local_server_address)

    license_retry_count = args.retry
    license_retry_wait = args.retry_wait

    await run_unity_command(license_retry_count, license_retry_wait,
        "-runTests", "-batchMode", "-projectPath", f'"{integration_project_path}"',
        "-buildTarget", args.platform,
        "-testCategory", "integration", "-testPlatform", args.platform,
        "-testResults", f"tmp/{args.platform}_results.xml", "-logFile", "-",
    )

    mock_server.terminate()
    t.join()

    pass

if __name__ == "__main__":
    task = main()
    res = asyncio.get_event_loop().run_until_complete(task)
    exit(res)
