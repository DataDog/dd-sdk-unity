#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import asyncio
import os
import re
import subprocess
import time
from saxonche import PySaxonProcessor
from typing import Optional

UNITY_LICENSE_ERROR = "No valid Unity Editor license found. Please activate your license."
LICENSE_STATE_RE = re.compile(r'License lease state: "\w+" with token: "(?P<token>.+)"')

def start_android_emulator():
    pass

def get_unity_path(version: str = "2022.3.42f1"):
    if "UNITY_PATH" in os.environ and os.environ['UNITY_PATH'] is not None:
        return os.environ['UNITY_PATH']
    # REVISIT: Only get the Mac version for now
    return f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity"

def get_license_server_path():
    # REVISIT: Only get the Mac version for now
    unity_home = "/Applications/Unity/Hub/Editor/2022.3.42f1/Unity.app/Contents"
    if "UNITY_HOME" in os.environ:
        unity_home = os.environ["UNITY_HOME"]
    return f"{unity_home}/Frameworks/UnityLicensingClient.app/Contents/MacOS/Unity.Licensing.Client"

async def _read_stream(stream, callback):
    while True:
        line = await stream.readline()
        if line:
            callback(line.decode('utf8'))
        else:
            break

async def get_unity_license() -> Optional[str]:
    token = None
    def process_stdout(line):
        m = LICENSE_STATE_RE.match(line)
        print(f'[uls]  {line}')
        nonlocal token
        if m is not None:
            token = m.group("token")

    env = os.environ.copy()
    cmd = f'{get_license_server_path()} --acquire-floating'
    process = await asyncio.create_subprocess_shell (cmd,
                                   env=env,
                                   stdout=asyncio.subprocess.PIPE,
                                   )
    await asyncio.wait([
        _read_stream(process.stdout, process_stdout)
    ])

    await process.wait()

    return token

async def return_unity_license(token: str):
    cmd = f'{get_license_server_path()} --return-floating {token}'
    process = await asyncio.create_subprocess_shell (cmd, env=env, stdout=asyncio.subprocess.STDOUT)

    return_code = await process.wait()

    return return_code

def transform_nunit_to_junit(nunit_file: str, junit_file: str):
    with PySaxonProcessor(license=False) as proc:
        xsltproc = proc.new_xslt30_processor()
        xsltproc.transform_to_file(source_file=nunit_file, stylesheet_file="nunit3-junit.xslt", output_file=junit_file)


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
            nonlocal did_see_license_error
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
