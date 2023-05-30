#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import subprocess
import sys
import json
import os
import threading

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


def main():
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

    run_unity_command(
        "-runTests", "-batchMode", "-projectPath", integration_project_path,
        "-testCategory", "integration", "-testPlatform", "android",
        "-testResults", "tmp/results.xml", "-logFile", "-"
    )

    mock_server.terminate()
    t.join()

    pass

if __name__ == "__main__":
    main()
