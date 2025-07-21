"""
High-level interface for the Android emulator; allows you to specify the target AVD
with AndroidDeviceSpec and use `with run_android_device(spec):` to provision and boot
the appropriate emulator instance for the duration of the `with` block.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import platform
import random
import subprocess
from dataclasses import dataclass
from contextlib import contextmanager
from typing import Optional, Generator

from common.log import get_default_logger

from .sdkmanager import AndroidSdkManager
from .avdmanager import AvdManager, Avd
from .adb import Adb, AdbDevice
from .emulator import AndroidEmulator


@dataclass
class AndroidDeviceSpec:
    api_level: int
    device: str
    tag: str
    abi: str

    @property
    def system_image_package(self) -> str:
        return f'system-images;android-{self.api_level};{self.tag};{self.abi}'
    
    @property
    def default_avd_name(self) -> str:
        return f"dd-api-{self.api_level}-{self.device.replace('_', '-')}"
    
    @classmethod
    def default(cls, api_level: int, device: str) -> 'AndroidDeviceSpec':
        abi = 'arm64-v8a' if platform.machine().lower().startswith('arm64') else 'x86_64'
        return cls(
            api_level=api_level,
            device=device,
            tag='google_apis',
            abi=abi,
        )


@contextmanager
def run_android_device(spec: AndroidDeviceSpec) -> Generator[AdbDevice, None, None]:
    log = get_default_logger()
    log.info('Preparing an emulated Android device...')
    log.info(f'- API Level: {spec.api_level}')
    log.info(f'- Device profile: {spec.device}')
    log.info(f'- Tag: {spec.tag}')
    log.info(f'- ABI: {spec.abi}')

    # Ensure that we have all required Android SDK tools preinstalled
    sdkmanager = AndroidSdkManager.require()
    log.info(f'Using sdkmanager at: {sdkmanager.path}')
    avdmanager = AvdManager.require()
    log.info(f'Using avdmanager at: {avdmanager.path}')
    adb = Adb.require()
    log.info(f'Using adb at: {adb.path}')
    emulator = AndroidEmulator.require()
    log.info(f'Using emulator at: {adb.path}')

    # Use sdkmanager to ensure that we have the required system image installed
    installed_packages = sdkmanager.list_installed_packages()
    existing_system_package = next((x for x in installed_packages if x.path == spec.system_image_package), None)
    if not existing_system_package:
        log.info(f'Installing Android package {spec.system_image_package}...')
        sdkmanager.install_package(spec.system_image_package)
    log.info(f'{spec.system_image_package} is installed.')

    # Use avdmanager to ensure that we have an AVD that satisfies our desired device details
    avds = avdmanager.list_avds()
    def _is_match(avd: Avd) -> bool:
        return (avd.api_level, avd.device, avd.tag, avd.abi) == (spec.api_level, spec.device, spec.tag, spec.abi)
    existing_avd = next((x for x in avds if _is_match(x)), None)
    avd_name = ''
    if existing_avd:
        avd_name = existing_avd.name
    else:
        avd_name = spec.default_avd_name
        log.info(f'Creating new AVD {avd_name} to satisfy device spec...')
        avdmanager.create_avd(avd_name, spec.system_image_package, spec.device)
    log.info(f'Using AVD {avd_name}.')

    # Pick a random port to start our emulated device on
    emulator_port = _choose_random_emulator_port()
    device_name = f'emulator-{emulator_port}'

    # Use the Android emulator to start up an emulated device
    emulator_args = [
        emulator.path,
        '-avd', avd_name,
        '-port', str(emulator_port),
        '-verbose',
        '-show-kernel',
        '-no-audio',
        '-netdelay', 'none',
        '-no-snapshot',
        '-wipe-data',
    ]
    emulator_process = subprocess.Popen(
        emulator_args,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.STDOUT,
        universal_newlines=True,
    )

    # We now have an emulator process running: make sure it gets cleaned up on exit
    device: Optional[AdbDevice] = None
    try:
        # Wait for the device to become available in adb
        log.info(f'{device_name} started; PID {emulator_process.pid}.')
        device = adb.wait_for_device(device_name)

        # Wait until 'sys.boot_completed' is 1
        log.info(f'{device.name} is running; waiting for boot...')
        device.wait_for_boot()

        # Device is ready to use; yield it to the caller
        log.info(f'{device.name} is ready!')
        yield device
    finally:
        # Attempt a clean shutdown with 'adb emu kill', which should cause our emulator
        # process to shut down as well
        killed_device = False
        if device:
            try:
                log.info(f'Cleanly shutting down emulator {device.name}...')
                device.emu_kill()
                killed_device = True
            except Exception as e:
                log.error(f'Failed to shut down emulator: {e}')
        
        # If we couldn't shut down cleanly (either because 'adb emu kill' failed, or
        # because we never resolved the device in adb to begin with), terminate the
        # emulator process directly
        if not killed_device:
            log.warning('Emulator could not be shut down via adb; sending SIGKILL to emulator process')
            emulator_process.kill()

        # Block until the emulator process has exited
        log.info(f'Waiting for emulator (PID {emulator_process.pid}) to shut down...')
        emulator_exitcode = emulator_process.wait(30.0)
        log.info(f'emulator exited with status code {emulator_exitcode}.')


def _choose_random_emulator_port() -> int:
    # Even ports in this range (inclusive) are valid for emulated Android devices
    min_valid_port = 5554
    max_valid_port = 5682
    valid_ports = range(min_valid_port, max_valid_port+1, 2)

    # The Android emulator will auto-assign ports sequentially; leave a chunk of them
    # untouched so we don't conflict with any user-managed devices
    num_reserved_ports = 16
    candidate_ports = valid_ports[num_reserved_ports:]

    # Pick a random port from that subset
    return random.choice(candidate_ports)
