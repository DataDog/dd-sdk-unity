"""
Utility code providing a platform-agnostic abstraction layer for running builds on
devices or simulators.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
from dataclasses import dataclass
from contextlib import contextmanager
from typing import Generator, Callable, Optional, ContextManager

from common.android import AndroidDeviceSpec, run_android_device, Adb
from common.apple import AppleDeviceSpec, run_apple_device, Xcrun, IDeviceSyslog
from common.shell import OutputHandlerFunc


__default_ios_device__ = AppleDeviceSpec('iOS 17.4', 'iPhone 15 Pro')
__default_android_device__ = AndroidDeviceSpec.default(api_level=33, device='pixel_4')


@dataclass
class TargetDevice:
    """
    Interface for a device on which packaged app builds can be installed and run.
    """
    platform: str       # e.g. 'android', 'ios'
    is_simulated: bool  # True for an emulated device, False for physical hardware
    device_id: str      # UDID for iOS; ADB serial or device name for Android; etc.

    def uninstall_app(self, app_bundle_id: str):
        if self.platform == 'ios':
            xcrun = Xcrun.require()
            if self.is_simulated:
                xcrun.simctl.uninstall(self.device_id, app_bundle_id)
            else:
                xcrun.devicectl.uninstall(self.device_id, app_bundle_id)
        else:
            assert self.platform == 'android'
            adb = Adb.require()
            adb.uninstall(self.device_id, app_bundle_id)

    def install_app(self, app_bundle_path: str):
        if self.platform == 'ios':
            xcrun = Xcrun.require()
            if self.is_simulated:
                xcrun.simctl.install(self.device_id, app_bundle_path)
            else:
                xcrun.devicectl.install(self.device_id, app_bundle_path)
        else:
            assert self.platform == 'android'
            adb = Adb.require()
            adb.install(self.device_id, app_bundle_path)
    
    def launch_app(self, app_bundle_id: str):
        if self.platform == 'ios':
            xcrun = Xcrun.require()
            if self.is_simulated:
                xcrun.simctl.launch(self.device_id, app_bundle_id)
            else:
                xcrun.devicectl.launch(self.device_id, app_bundle_id)
        else:
            assert self.platform == 'android'
            adb = Adb.require()
            adb.launch(self.device_id, app_bundle_id, 'com.unity3d.player.UnityPlayerActivity')

    def tail_logs(self, output_handler: Optional[OutputHandlerFunc]):
        if self.platform == 'ios':
            if self.is_simulated:
                xcrun = Xcrun.require()
                xcrun.simctl.tail_logs(self.device_id, 'senderImagePath CONTAINS[c] "UnityFramework"', output_handler)
            else:
                idevicesyslog = IDeviceSyslog.require()
                idevicesyslog.run(self.device_id, 'UnityFramework', output_handler)
        else:
            assert self.platform == 'android'
            adb = Adb.require()
            filters = [
                'Unity:V',
                'IL2CPP:V',
                'Datadog:V',
                'OkHttp:V',
                'System.err:V',
                'AndroidRuntime:E',
                '*:S',
            ]
            adb.tail_logs(self.device_id, filters, output_handler)


@contextmanager
def acquire_device(platform: str, use_simulator: bool) -> Generator[TargetDevice, None, None]:
    context_func: Optional[Callable[[], ContextManager[TargetDevice]]] = None
    if platform == 'android':
        if use_simulator:
            context_func = _run_android_simulator
        else:
            context_func = _acquire_android_device
    elif platform == 'ios':
        if use_simulator:
            context_func = _run_ios_simulator
        else:
            context_func = _acquire_ios_device

    if not context_func:
        s = ' simulated' if use_simulator else ''
        raise ValueError(f'Unsupported platform for running on{s} device: {platform}')

    with context_func() as target_device:
        yield target_device


@contextmanager
def _run_android_simulator() -> Generator[TargetDevice, None, None]:
    with run_android_device(__default_android_device__) as adb_device_name:
        yield TargetDevice(
            platform='android',
            is_simulated=True,
            device_id=adb_device_name,
        )


@contextmanager
def _acquire_android_device() -> Generator[TargetDevice, None, None]:
    # Use 'adb devices' to get a list of all connected Android devices that are ready
    adb = Adb.require()
    devices = adb.list_devices()
    if not devices:
        raise RuntimeError('adb devices lists no devices with ready (i.e. "device") status')
    
    # Take the first device that isn't an emulator
    device = next((d for d in devices if not d.name.startswith('emulator')), None)
    if not device:
        raise RuntimeError('No physical Android devices are connected and ready')
    
    yield TargetDevice(
        platform='android',
        is_simulated=False,
        device_id=device.name,
    )

@contextmanager
def _run_ios_simulator() -> Generator[TargetDevice, None, None]:
    with run_apple_device(__default_ios_device__) as udid:
        yield TargetDevice(
            platform='ios',
            is_simulated=True,
            device_id=udid,
        )


@contextmanager
def _acquire_ios_device() -> Generator[TargetDevice, None, None]:
    # Use 'xcrun devicectl list devices' to get a list of physical iOS devices
    xcrun = Xcrun.require()
    devices = xcrun.devicectl.list_devices()
    if not devices:
        raise RuntimeError('xcrun devicectl reports no available devices!')

    # Take the first device that's paired, i.e. ready to use
    device = next((d for d in devices if d.connection_properties.pairing_state == 'paired'), None)
    if not device:
        raise RuntimeError('No available iOS devices are paired')
    
    yield TargetDevice(
        platform='ios',
        is_simulated=False,
        device_id=device.hardware_properties.udid,
    )
