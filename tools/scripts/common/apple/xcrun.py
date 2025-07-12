"""
Utility code for invoking xcrun, which provides commands like simctl for managing and
creating iOS simulators.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import platform
import subprocess
import json
from typing import List, Dict

from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel

from common.shell import capture_output


class SimctlRuntimeListItem(BaseModel):
    version: str
    identifier: str
    platform: str
    name: str


class SimctlRuntimeList(BaseModel):
    runtimes: List[SimctlRuntimeListItem]


class SimctlDeviceListItem(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel)

    udid: str
    device_type_identifier: str
    state: str
    name: str


class SimctlDeviceList(BaseModel):
    devices: Dict[str, List[SimctlDeviceListItem]]


class Simctl(object):

    def list_available_runtimes(self) -> List[SimctlRuntimeListItem]:
        stdout, _ = capture_output('xcrun', 'simctl', 'list', 'runtimes', 'available', '--json')
        data = json.loads(stdout)
        result = SimctlRuntimeList(**data)
        return result.runtimes
    
    def list_available_devices(self) -> Dict[str, List[SimctlDeviceListItem]]:
        stdout, _ = capture_output('xcrun', 'simctl', 'list', 'devices', 'available', '--json')
        data = json.loads(stdout)
        result = SimctlDeviceList(**data)
        return result.devices
    
    def shutdown(self, udid: str, timeout_seconds: float = 30.0):
        args = ['xcrun', 'simctl', 'shutdown', udid]
        subprocess.check_call(args, timeout=timeout_seconds)

    def boot(self, udid: str, timeout_seconds: float = 30.0):
        args = ['xcrun', 'simctl', 'boot', udid]
        subprocess.check_call(args, timeout=timeout_seconds)

    def wait_for_boot(self, udid: str, timeout_seconds: float = 30.0):
        args = ['xcrun', 'simctl', 'bootstatus', udid]
        subprocess.check_call(args, timeout=timeout_seconds, stdout=subprocess.DEVNULL)


class Xcrun(object):
    @property
    def simctl(self) -> Simctl:
        return Simctl()

    @classmethod
    def require(cls) -> 'Xcrun':
        try:
            output = subprocess.check_output(['xcrun', '--version'])
        except:
            message = 'xcode must be installed'
            if platform.system() != 'Darwin':
                message = 'building for Apple platforms is only supported on Mac OS'
            raise RuntimeError(f'xcrun not found in PATH: {message}')

        if not output.decode().startswith('xcrun version'):
            raise RuntimeError('Unexpected output from xcrun --version')        
        return cls()
