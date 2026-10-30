# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
High-level interface for the Android emulator; allows you to specify the target AVD
with AndroidDeviceSpec and use `with run_android_device(spec):` to provision and boot
the appropriate emulator instance for the duration of the `with` block.
"""
import os
import platform
import random
import subprocess
from collections.abc import Callable, Generator
from contextlib import contextmanager
from dataclasses import dataclass
from pathlib import Path

from dd_sdk_unity.scripts.common.android.adb import Adb, AdbDevice
from dd_sdk_unity.scripts.common.android.avdmanager import Avd, AvdManager
from dd_sdk_unity.scripts.common.android.emulator import AndroidEmulator
from dd_sdk_unity.scripts.common.android.sdkmanager import AndroidSdkManager
from dd_sdk_unity.scripts.common.log import get_default_logger


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
    def default(cls, api_level: int, device: str) -> AndroidDeviceSpec:
        abi = 'arm64-v8a' if platform.machine().lower().startswith('arm64') else 'x86_64'
        return cls(
            api_level=api_level,
            device=device,
            tag='google_apis',
            abi=abi,
        )


@contextmanager
def run_android_device(
    spec: AndroidDeviceSpec, *, keep_running: bool = False,
    on_started: Callable[[str], None] | None = None,
) -> Generator[AdbDevice]:
    log = get_default_logger()
    log.info('Preparing an emulated Android device...')
    log.info(f'- API Level: {spec.api_level}')
    log.info(f'- Device profile: {spec.device}')
    log.info(f'- Tag: {spec.tag}')
    log.info(f'- ABI: {spec.abi}')

    # Require SDK tools to be installed before preparing the system image.
    sdkmanager = AndroidSdkManager.require()
    log.info(f'Using sdkmanager at: {sdkmanager.path}')
    avdmanager = AvdManager.require()
    log.info(f'Using avdmanager at: {avdmanager.path}')
    adb = Adb.require()
    log.info(f'Using adb at: {adb.path}')
    emulator = AndroidEmulator.require()
    log.info(f'Using emulator at: {emulator.path}')

    # Only install the required system image; SDK tools are prerequisites.
    installed_packages = sdkmanager.list_installed_packages()
    if not any(package.path == spec.system_image_package for package in installed_packages):
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

    adb.start_server()

    # Pick a random port to start our emulated device on
    emulator_port = _choose_random_emulator_port()
    device_name = f'emulator-{emulator_port}'

    # Use the Android emulator to start up an emulated device
    emulator_args = [
        emulator.path,
        '-adb-path', adb.path,
        '-avd', avd_name,
        '-port', str(emulator_port),
        '-verbose',
        '-show-kernel',
        '-no-audio',
        '-no-boot-anim',
        '-netdelay', 'none',
        '-no-snapshot',
        '-wipe-data',
    ]
    # Keep emulator child tools on the same SDK's ADB client.
    environment = os.environ.copy()
    environment['PATH'] = os.pathsep.join(filter(None, [str(Path(adb.path).parent), environment.get('PATH')]))
    emulator_process = subprocess.Popen(
        emulator_args,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.STDOUT,
        stdin=subprocess.DEVNULL,
        universal_newlines=True,
        start_new_session=keep_running,
        env=environment,
    )

    # We now have an emulator process running: make sure it gets cleaned up on exit
    device: AdbDevice | None = None
    detached = False
    try:
        if on_started is not None:
            on_started(device_name)
        # Wait for the device to become available in adb
        log.info(f'{device_name} started; PID {emulator_process.pid}.')
        device = adb.wait_for_device(device_name, timeout_seconds=120)

        # Wait until 'sys.boot_completed' is 1
        log.info(f'{device.name} is running; waiting for boot...')
        device.wait_for_boot(timeout_seconds=120)

        # Device is ready to use; yield it to the caller
        log.info(f'{device.name} is ready!')
        yield device
        detached = keep_running
    finally:
        if not detached:
            _stop_android_device(emulator_process, device)


def _stop_android_device(process: subprocess.Popen[str], device: AdbDevice | None) -> None:
    log = get_default_logger()
    killed_device = False
    if device:
        try:
            log.info(f'Cleanly shutting down emulator {device.name}...')
            device.emu_kill()
            killed_device = True
        except Exception:
            log.exception('Failed to shut down emulator')
    if not killed_device:
        log.warning('Emulator could not be shut down via adb; sending SIGKILL to emulator process')
        process.kill()
    log.info(f'Waiting for emulator (PID {process.pid}) to shut down...')
    log.info(f'emulator exited with status code {process.wait(30.0)}.')


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
