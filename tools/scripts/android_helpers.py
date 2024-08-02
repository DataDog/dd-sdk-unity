#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import datetime
import io
import json
import os
import re
import subprocess
import time
from typing import Optional

AVD_TAG = 'google_apis'
AVD_ABI = 'arm64-v8a'

def _get_android_home() -> str:
    android_home = os.environ['ANDROID_HOME']
    if android_home is None:
        raise Exception('ANDROID_HOME is not set.')

    return android_home

def _get_avd_manager() -> str:
    return os.path.join(_get_android_home(), 'cmdline-tools', 'latest', 'bin', 'avdmanager')

def _get_sdk_manager() -> str:
    return os.path.join(_get_android_home(), 'cmdline-tools', 'latest', 'bin', 'sdkmanager')

def _get_emulator_command() -> str:
    return os.path.join(_get_android_home(), 'emulator', 'emulator')

def _run(args: list[str], write_std_out: bool = False) -> str:
    process = subprocess.Popen(args,
                               stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT,
                               start_new_session=True,
                               universal_newlines=True)
    output = io.StringIO()
    for line in process.stdout:
        if write_std_out:
            print(line)
        output.write(line)

    process.communicate()
    if process.returncode != 0:
        print(output.getvalue())
        raise Exception(f'{args[0]} exited with non-zero exit code: {process.returncode}')

    return output.getvalue()

def _emulator_exists(emulator_name: str) -> bool:
    print(f'Checking for existing Android emulator named {emulator_name}.')
    result = _run([_get_avd_manager(), 'list', 'avd', '--compact'])
    emulators = result.split('\n')

    return emulator_name in emulators

def _startEmulator(emulator_name: str) -> bool:
    print(f"Starting device {emulator_name}")

    process = subprocess.Popen(
        [
            _get_emulator_command(),
            f"@{emulator_name}",
            "-verbose",
            "-show-kernel",
            "-no-audio",
            "-netdelay",
            "none",
            "-no-snapshot",
            "-wipe-data",
        ],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.STDOUT,
        universal_newlines=True)

    launched = False
    timeout_time = datetime.datetime.now() + datetime.timedelta(minutes=5)
    while (datetime.datetime.now() < timeout_time):
        time.sleep(5)
        print("Checking if emulator is running...")
        devices = _get_running_devices()
        device = next((x[1] for x in devices.items() if x[1] == 'device'), None)
        if device is not None:
            print('Device running')
            launched = True
            break

    if launched:
        # Wait an additional 10 seconds to make sure the emulator has
        # some extra time to boot
        time.sleep(10)

    return launched

def _get_running_devices() -> dict[str, str]:
    device_pattern = r'^(?P<emulator>emulator-\d*)[\s+](?P<state>.*)'

    adb = os.path.join(_get_android_home(), 'platform-tools', 'adb')
    result = {}
    devices_log = _run([adb, 'devices'])
    for line in devices_log.split('\n'):
        match = re.search(device_pattern, line)
        if match is not None:
            result[match.group('emulator')] = match.group('state')

    return result

def launch_android_emulator(api_level: Optional[str], emulator_name: Optional[str], should_update: bool = True) -> bool:
    if api_level is None and emulator_name is None:
        print('Error in script -- must specify either Android API level or an emulator name')
        return False

    need_emulator_create = True
    if emulator_name is not None:
        if _emulator_exists(emulator_name):
            need_emulator_create = False
    elif api_level is not None:
        emulator_name = f"ci_emu_api_{api_level}"
        need_emulator_create = not _emulator_exists(emulator_name)

    if need_emulator_create:
        package = f"system-images;android-{api_level};{AVD_TAG};{AVD_ABI}"
        if should_update:
            print("Updating emlators with sdkmanager")
            # TODO: pipe in "Yes"
            _run([_get_sdk_manager(), "--verbose", "emulator"], True)

        print("Updating system-image packages")
        _run([_get_sdk_manager(), "--verbose", package], True)

        print("Creating device")
        _run([_get_avd_manager(), "create", "avd", "-n", emulator_name, "--package", package], True)

    devices = _get_running_devices()
    if bool(devices):
        print(f"{emulator_name} already started. Returning.")
        return True

    if _startEmulator(emulator_name):
        #TODO - need to force exit the script because the emulator process could still be running.
        return True


    return False

