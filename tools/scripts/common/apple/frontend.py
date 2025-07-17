"""
High-level interface for iOS simulators; allows you to specify the target device type
with AppleDeviceSpec and use `with run_apple_device(spec):` to provision and boot the
appropriate simulator for the duration of the `with` block.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
from dataclasses import dataclass
from contextlib import contextmanager
from typing import Generator

from common.log import get_default_logger

from .xcrun import Xcrun


@dataclass
class AppleDeviceSpec:
    runtime: str  # e.g. 'iOS 18.5'
    device: str  # e.g. 'iPhone 16 Pro'

    @property
    def xcode_destination(self) -> str:
        value = f'platform=iOS Simulator,name={self.device}'
        if self.runtime.startswith('iOS '):
            ios_version = self.runtime[len('iOS '):]
            value += f',OS={ios_version}'
        return value


@contextmanager
def run_apple_device(spec: AppleDeviceSpec) -> Generator[str, None, None]:
    log = get_default_logger()
    log.info('Preparing an emulated Apple device...')
    log.info(f'- Runtime: {spec.runtime}')
    log.info(f'- Device: {spec.device}')

    xcrun = Xcrun.require()

    # Verify that our target runtime is installed
    runtimes = xcrun.simctl.list_available_runtimes()
    runtime = next((x for x in runtimes if x.name == spec.runtime), None)
    if not runtime:
        raise RuntimeError(f'Runtime {spec.runtime} is not available; please install it via Xcode -> Settings -> Components')
    log.info(f'Runtime is available: {runtime.identifier}')

    # Verify that our desired device is available for that runtime
    devices_by_runtime_identifier = xcrun.simctl.list_available_devices()
    devices = devices_by_runtime_identifier.get(runtime.identifier, [])
    device = next((x for x in devices if x.name == spec.device), None)
    if not device:
        raise RuntimeError(f'Device {spec.device} is not available for runtime {spec.runtime}')
    log.info(f'Device is available: {device.udid}')

    if device.state != 'Shutdown':
        log.info(f'Current device state is {device.state}; shutting it down for clean boot...')
        xcrun.simctl.shutdown(device.udid)

    # Boot the device, yield once it's ready, and clean up when finished
    xcrun.simctl.boot(device.udid)
    try:
        log.info(f'Device {device.udid} ({spec.device} on {spec.runtime}) started; waiting for boot...')
        xcrun.simctl.wait_for_boot(device.udid)
        log.info(f'{device.udid} is ready!')
        yield device.udid
    finally:
        log.info(f'Cleanly shutting down device {device.udid}...')
        xcrun.simctl.shutdown(device.udid)
        log.info('Done.')
