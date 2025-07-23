"""
Utility code for invoking xcrun, which provides commands like simctl for managing and
creating iOS simulators.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import platform
import subprocess
import threading
import json
from typing import List, Dict, Optional

from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel

from common.log import get_default_logger
from common.shell import capture_output, run_cmd, OutputHandlerFunc


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

    def uninstall(self, udid: str, app_bundle_id: str, timeout_seconds: float = 30.0):
        args = ['xcrun', 'simctl', 'uninstall', udid, app_bundle_id]
        subprocess.check_call(args, timeout=timeout_seconds)

    def install(self, udid: str, app_bundle_path: str, timeout_seconds: float = 60.0):
        args = ['xcrun', 'simctl', 'install', udid, app_bundle_path]
        subprocess.check_call(args, timeout=timeout_seconds)

    def launch(self, udid: str, app_bundle_id: str, timeout_seconds: float = 30.0):
        args = ['xcrun', 'simctl', 'launch', udid, app_bundle_id]
        subprocess.check_call(args, timeout=timeout_seconds)

    def tail_logs(self, udid: str, predicate: str, output_handler: Optional[OutputHandlerFunc]):
        log = get_default_logger()
        args = ['xcrun', 'simctl', 'spawn', udid, 'log', 'stream', '--style', 'syslog', '--predicate', predicate]

        def _log_main():
            def _read(line: str, is_stderr: bool):
                log.info(line)
                if output_handler:
                    output_handler(line, is_stderr)
            run_cmd(*args, raise_on_nonzero_exitcode=True, output_handler=_read)

        threading.Thread(target=_log_main, daemon=True).start()


class DevicectlConnectionProperties(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel)

    pairing_state: str


class DevicectlDeviceProperties(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel)

    developer_mode_status: str
    name: str
    os_build_update: str
    os_version_number: str


class DevicectlHardwareProperties(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel)

    device_type: str
    marketing_name: str
    platform: str
    product_type: str
    udid: str


class DevicectlDevice(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel)

    identifier: str
    visibility_class: str

    connection_properties: DevicectlConnectionProperties
    device_properties: DevicectlDeviceProperties
    hardware_properties: DevicectlHardwareProperties
    

class DevicectlListDevicesResult(BaseModel):
    devices: List[DevicectlDevice]


class DevicectlListDevicesCommandOutput(BaseModel):
    result: DevicectlListDevicesResult


class Devicectl(object):
    
    def list_devices(self) -> List[DevicectlDevice]:
        stdout, _ = capture_output('xcrun', 'devicectl', '--json-output', '-', 'list', 'devices')
        data = json.loads(stdout)
        output = DevicectlListDevicesCommandOutput(**data)
        return output.result.devices

    def uninstall(self, udid: str, app_bundle_id: str, timeout_seconds: float = 30.0):
        args = ['xcrun', 'devicectl', 'device', 'uninstall', 'app', '--device', udid, app_bundle_id]
        subprocess.check_call(args, timeout=timeout_seconds)

    def install(self, udid: str, app_bundle_path: str, timeout_seconds: float = 60.0):
        args = ['xcrun', 'devicectl', 'device', 'install', 'app', '--device', udid, app_bundle_path]
        subprocess.check_call(args, timeout=timeout_seconds)

    def launch(self, udid: str, app_bundle_id: str, timeout_seconds: float = 30.0):
        args = ['xcrun', 'devicectl', 'device', 'process', 'launch', '--device', udid, '--terminate-existing', app_bundle_id]
        subprocess.check_call(args, timeout=timeout_seconds)


class Xcrun(object):
    @property
    def simctl(self) -> Simctl:
        return Simctl()
    
    @property
    def devicectl(self) -> Devicectl:
        return Devicectl()

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
