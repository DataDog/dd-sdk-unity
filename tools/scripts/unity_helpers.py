#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import os
import subprocess


def get_unity_path(version: str = "2021.3.24f1"):
    # REVISIT: Only get the Mac version for now
    return '/Applications/Unity/Hub/Editor/2021.3.24f1/Unity.app/Contents/MacOS/Unity'

def run_unity_command(*args):
    process = subprocess.run([get_unity_path(), *args], stdout=subprocess.PIPE, universal_newlines=True)
    for line in process.stdout:
        print(line)
