#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import argparse
import asyncio
import os
import re
import time
from saxonche import PySaxonProcessor # type: ignore
from typing import Optional

UNITY_HUB_PATH = "/Applications/Unity\\ Hub.app/Contents/MacOS/Unity\\ Hub"
UNITY_PATH = "/Applications/Unity/Hub/Editor/{unity_version}/Unity.app/Contents"

UNITY_LICENSE_ERROR = "No valid Unity Editor license found. Please activate your license."
LICENSE_STATE_RE = re.compile(r'License lease state: "\w+" with token: "(?P<token>.+)"')

DEFAULT_UNITY_VERSION = "2022.3"

global unity_version
unity_version = DEFAULT_UNITY_VERSION

def start_android_emulator():
    pass

async def _run_process(cmd: str) -> str:
    result = ""
    def process_stdout(line):
        nonlocal result
        result += line

    env = os.environ.copy()
    process = await asyncio.create_subprocess_shell (cmd,
                                   env=env,
                                   stdout=asyncio.subprocess.PIPE,
                                   )
    await asyncio.wait([
        _read_stream(process.stdout, process_stdout)
    ])

    await process.wait()

    return result

async def get_full_unity_version(version_partial: str, force_upgrade: bool = False, update_global = True):
    global unity_version
    if len(version_partial.split(".")) == 3:
        # Not a version partial. Return the whole version
        if update_global:
            unity_version = version_partial
        return version_partial

    version = None
    if not force_upgrade:
        # check to see if we have a matching version first
        installed_cmd = f'{UNITY_HUB_PATH} -- --headless editors --installed'
        installed_output = await _run_process(installed_cmd)

        matching_versions = list(filter(lambda ver: ver.startswith(version_partial),
                                   [line.split(" ")[0] for line in installed_output.split("\n")]))
        if len(matching_versions) > 0:
            best_version = sorted(matching_versions)[-1]
            version = best_version

    if version is None:
        releases_cmd = f'{UNITY_HUB_PATH} -- --headless editors --releases'
        releases_output = await _run_process(releases_cmd)

        matching_versions = list(filter(lambda ver: ver.startswith(version_partial),
                                    [line.split(" ")[0] for line in releases_output.split("\n")]))
        if len(matching_versions) > 0:
            best_version = sorted(matching_versions)[-1]
            version = best_version

    if version is not None and update_global:
        unity_version = version

    return version

def get_unity_home():
    global unity_version
    if "UNITY_VERSION" in os.environ:
        unity_version = os.environ["UNITY_VERSION"]
    unity_home = UNITY_PATH.format(unity_version=unity_version)
    if "UNITY_HOME" in os.environ:
        unity_home = os.environ["UNITY_HOME"]
    return unity_home

def get_unity_path(version: str = "2022.3.42f1"):
    if "UNITY_PATH" in os.environ and os.environ['UNITY_PATH'] is not None:
        return os.environ['UNITY_PATH']
    # REVISIT: Only get the Mac version for now
    return f"{get_unity_home()}/MacOS/Unity"

def get_license_server_path():
    # REVISIT: Only get the Mac version for now
    return f"{get_unity_home()}/Frameworks/UnityLicensingClient.app/Contents/MacOS/Unity.Licensing.Client"

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
    process = await asyncio.create_subprocess_shell (cmd, stdout=asyncio.subprocess.STDOUT)

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

async def main():
    argparser = argparse.ArgumentParser()
    subparser = argparser.add_subparsers(dest='command', title='subcommands', description='valid subcommands')

    version_subparser = subparser.add_parser('get-version')
    version_subparser.add_argument('--version', default=DEFAULT_UNITY_VERSION, help="Full or version partial to match against")
    args = argparser.parse_args()

    if args.command == 'get-version':
        version = await get_full_unity_version(args.version, False, False)
        print(version)


if __name__ == "__main__":
    task = main()
    res = asyncio.get_event_loop().run_until_complete(task)
    exit(res)
