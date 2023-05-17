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

from unity_helpers import run_unity_command

integration_project_path = "../../../samples/IntegrationTest"

def run_mock_server():
    mock_server_dir = "../mock_server/"
    run_server_command = "./venv/bin/python3"
    return subprocess.run([run_server_command, "app.py"], cwd=mock_server_dir, universal_newlines=True)

def main():
    mock_server = run_mock_server()

    pass

if __name__ == "__main__":
    main()
