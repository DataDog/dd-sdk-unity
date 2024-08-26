#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import asyncio
import os
import subprocess
import time

UNITY_LICENSE_ERROR = "No valid Unity Editor license found. Please activate your license."

def start_android_emulator():
    pass

def get_unity_path(version: str = "2022.3.42f1"):
    if "UNITY_PATH" in os.environ and os.environ['UNITY_PATH'] is not None:
        return os.environ['UNITY_PATH']
    # REVISIT: Only get the Mac version for now
    return f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"

async def _read_stream(stream, callback):
    while True:
        line = await stream.readline()
        if line:
            callback(line.decode('utf8'))
        else:
            break

async def run_unity_command(license_retry_attempts: int, license_retry_timeout_seconds: float, *args):
    current_run_attempt = 0
    while True:
        should_retry = False
        did_see_license_error = False
        # Modify environment variables to ensure cocoapods works
        env = os.environ.copy()
        env['GEM_HOME'] = f"{env['HOME']}/.gem"
        env['PATH'] = f"{env['HOME']}/.gem/ruby/2.6.0/bin:{env['PATH']}"
        cmd = " ".join([get_unity_path(), *args])
        print(f'Running: {cmd}')
        process = await asyncio.create_subprocess_shell (cmd,
                                   env=env,
                                   stdout=asyncio.subprocess.PIPE,
                                   )

        def process_stdout(line):
            if UNITY_LICENSE_ERROR in line:
                did_see_license_error = True
            print(f"[unity] {line}", end='')

        await asyncio.wait([
            _read_stream(process.stdout, process_stdout)
        ])

        return_code = await process.wait()

        if return_code != 0 and did_see_license_error:
            if current_run_attempt < license_retry_attempts:
                should_retry = True
                current_run_attempt += 1
                print(f"License aquisition failed. Sleeping for {license_retry_timeout_seconds} seconds")
                time.sleep(license_retry_timeout_seconds)

        if not should_retry:
            print(f"Unity returned {return_code}")
            return return_code
