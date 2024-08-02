#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import os
import subprocess
import time

UNITY_LICENSE_ERROR = "No valid Unity Editor license found. Please activate your license."

def start_android_emulator():
    pass

def get_unity_path(version: str = "2022.3.29f1"):
    # REVISIT: Only get the Mac version for now
    return f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"

def run_unity_command(license_retry_attempts: int, license_retry_timeout_seconds: float, *args,):
    current_run_attempt = 0
    while True:
        should_retry = False
        did_see_license_error = False
        # Modify environment variables to ensure cocoapods works
        env = os.environ.copy()
        env['GEM_HOME'] = f"{env['HOME']}/.gem"
        env['PATH'] = f"{env['HOME']}/.gem/ruby/2.6.0/bin:{env['PATH']}"
        process = subprocess.Popen([get_unity_path(), *args],
                                   env=env,
                                   stdout=subprocess.PIPE,
                                   universal_newlines=True)
        for line in process.stdout:
            if UNITY_LICENSE_ERROR in line:
                did_see_license_error = True
            print(f"[unity] {line}", end='')

        if process.returncode != 0 and did_see_license_error:
            if current_run_attempt < license_retry_attempts:
                should_retry = True
                current_run_attempt += 1
                print(f"License aquisition failed. Sleeping for {license_retry_timeout_seconds} seconds")
                time.sleep(license_retry_timeout_seconds)

        if not should_retry:
            return process.returncode
