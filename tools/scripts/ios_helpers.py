#!/usr/bin/python3

# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.
# -----------------------------------------------------------

import fileinput
import io
import json
import selectors
import subprocess
from typing import Optional

IOS_DEVICE_SDK=988
IOS_SIMULATOR_SDK=989

class IosSimulator:
    def __init__(self, json) -> None:
        self.name = json['name']
        self.uuid = json['udid']
        self.device_type_identifier = json['deviceTypeIdentifier']
        self.state = json['state']

def _xcrun(*args) -> str:
    process = subprocess.Popen(['xcrun', *args],
                               stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT,
                               start_new_session=True,
                               universal_newlines=True)
    output = io.StringIO()
    for line in process.stdout:
        output.write(line)

    process.communicate()
    if process.returncode != 0:
        print(output.getvalue())
        raise Exception(f'xcrun exited with non-zero exit code: {process.returncode}')

    return output.getvalue()

def _switch_sdk_target(settings_path: str, target: int):
    with fileinput.input(settings_path, inplace=True) as f:
        for line in f:
            if line.startswith("  iPhoneSdkVersion:"):
                print(f"  iPhoneSdkVersion: {target}")
            else:
                print(line, end='')

def switch_to_simulator_target(settings_path: str):
    _switch_sdk_target(settings_path, IOS_SIMULATOR_SDK)

def switch_to_device_target(settings_path: str):
    _switch_sdk_target(settings_path, IOS_DEVICE_SDK)

def get_ios_simulators() -> dict[str, list[IosSimulator]]:
    output = _xcrun('simctl', 'list', '--json', 'devices', 'available')
    output_json = json.loads(output)

    devices = output_json['devices']
    mappedDevices = {}
    for key, value in devices.items():
        simulators = [IosSimulator(e) for e in value]
        mappedDevices[key] = simulators

    return mappedDevices

def launch_ios_simulator(sdk: str, device_name: Optional[str]) -> bool:
    simulator_list = get_ios_simulators()

    # "Fuzzy" SDK match
    sdk_devices = next((x[1] for x in simulator_list.items() if sdk in x[0]), None)
    if sdk_devices is None:
        print(f'Found no runtimes matching {sdk}')
        return False

    if device_name is None:
        device = next(iter(sdk_devices), None)
    else:
        device = next(x for x in sdk_devices if device_name in x.name)

    if device is None:
        print(f'Found no ddvices matching {device_name} for {sdk}')
        return False

    if device.state == 'Booted':
        print(f'Device {device.name} is already booted.')
        return True

    print(f'Launching {device.name}...')
    #subprocess.call(['xcrun', 'simctl', 'boot', device.uuid])
    output = _xcrun('simctl', 'boot', device.uuid)

    return True
