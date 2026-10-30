# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for invoking adb (Android Debug Bridge), which manages connected Android
devices.
"""
import subprocess
import time
from dataclasses import dataclass

from dd_sdk_unity.scripts.common.android.util import resolve_android_binary


@dataclass
class AdbDevice:
    adb_path: str
    name: str

    def wait_for_boot(self, timeout_seconds: float = 30.0, interval_seconds: float = 0.5) -> None:
        deadline = time.monotonic() + timeout_seconds
        failure = None
        while (remaining := deadline - time.monotonic()) > 0:
            args = [self.adb_path, '-s', self.name, 'shell', 'getprop', 'sys.boot_completed']
            try:
                output = subprocess.check_output(args, timeout=min(1.0, remaining))
                if output.decode().strip() == '1':
                    return
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                failure = error
            time.sleep(min(interval_seconds, max(0, deadline - time.monotonic())))
        raise TimeoutError(f'ADB device {self.name} failed to complete boot after {timeout_seconds} seconds') from failure
            
    def emu_kill(self, timeout_seconds: float = 30.0) -> None:
        args = [self.adb_path, '-s', self.name, 'emu', 'kill']
        subprocess.check_call(args, timeout=timeout_seconds)

    def reverse_port(self, port: int) -> bool:
        """Create a reverse tunnel without taking ownership of an existing one."""
        endpoint = f'tcp:{port}'
        output = subprocess.check_output(
            [self.adb_path, '-s', self.name, 'reverse', '--list'], text=True, timeout=10,
        )
        for line in output.splitlines():
            fields = line.split()
            if len(fields) == 3 and fields[1] == endpoint:
                if fields[2] != endpoint:
                    raise RuntimeError(f'{self.name} already forwards {endpoint} to {fields[2]}')
                return False
        subprocess.check_output(
            [self.adb_path, '-s', self.name, 'reverse', '--no-rebind', endpoint, endpoint],
            timeout=10,
        )
        return True

    def remove_reverse_port(self, port: int) -> None:
        endpoint = f'tcp:{port}'
        try:
            subprocess.check_output(
                [self.adb_path, '-s', self.name, 'reverse', '--remove', endpoint], timeout=10,
            )
        except subprocess.CalledProcessError:
            # An ADB restart or Unity cleanup may have already removed our mapping.
            output = subprocess.check_output(
                [self.adb_path, '-s', self.name, 'reverse', '--list'], text=True, timeout=10,
            )
            if any(len(fields := line.split()) == 3 and fields[1] == endpoint
                   for line in output.splitlines()):
                raise


@dataclass
class Adb:
    """
    Wrapper for $ANDROID_HOME/platform-tools/adb.
    """
    path: str

    def start_server(self) -> None:
        subprocess.check_call([self.path, 'start-server'], timeout=10)

    def list_devices(self) -> list[AdbDevice]:
        output = subprocess.check_output([self.path, 'devices'], text=True, timeout=10)
        return [AdbDevice(self.path, fields[0]) for line in output.splitlines()
                if len(fields := line.split()) == 2 and fields[1] == 'device']

    def wait_for_device(self, device_name: str, timeout_seconds: float = 30.0) -> AdbDevice:
        args = [self.path, '-s', device_name, 'wait-for-device']
        deadline = time.monotonic() + timeout_seconds
        failure = None
        while (remaining := deadline - time.monotonic()) > 0:
            try:
                subprocess.check_call(args, timeout=remaining)
                return AdbDevice(adb_path=self.path, name=device_name)
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                failure = error
            time.sleep(min(0.5, max(0, deadline - time.monotonic())))
        raise TimeoutError(f'ADB device {device_name} was unavailable after {timeout_seconds} seconds') from failure

    @classmethod
    def require(cls) -> Adb:
        path, error_message = resolve_android_binary('platform-tools', 'adb')
        if error_message:
            raise RuntimeError(f'Failed to find adb: {error_message}')
        return cls(path)
