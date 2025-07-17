"""
Utility code for invoking adb (Android Debug Bridge), which manages connected Android
devices.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import re
import time
import subprocess
from typing import List

from dataclasses import dataclass

from ..shell import capture_output
from .util import resolve_android_binary


@dataclass
class AdbDevice:
    adb_path: str
    name: str

    def wait_for_boot(self, timeout_seconds: float = 30.0, interval_seconds = 0.5):
        started_at = time.time()
        while True:
            args = [self.adb_path, '-s', self.name, 'shell', 'getprop', 'sys.boot_completed']
            output = subprocess.check_output(args, timeout=2.0)
            if output.decode().strip() == '1':
                break
            elapsed = time.time() - started_at
            if elapsed + interval_seconds > timeout_seconds:
                raise TimeoutError(f'ADB device {self.name} failed to complete boot after {timeout_seconds} seconds')
            
    def emu_kill(self, timeout_seconds: float = 30.0):
        args = [self.adb_path, '-s', self.name, 'emu', 'kill']
        subprocess.check_call(args, timeout=timeout_seconds)


@dataclass
class Adb:
    """
    Wrapper for $ANDROID_HOME/platform-tools/adb.
    """
    path: str

    def restart_server(self):
        """
        Restarts the adb daemon to ensure that subsequent adb commands will be able to
        connect without error.

        When Unity runs an Android build, it aggressively kills any existing adb server
        instances in order to ensure that its adb commands use the version it expects.
        (This behavior is controlled via the "Kill external ADB instances" setting in
        Preferences -> External Tools -> Android.) As a result, if we run `adb devices`
        immediately after completing an Android build in Unity, the command may fail.

        Calling restart_server() allows us to ensure that _our_ version of adb is in a
        known working state before we proceed with adb commands.
        """
        # Kill and restart the adb daemon
        subprocess.check_call([self.path, 'kill-server'])
        subprocess.check_call([self.path, 'start-server'])

        # Block until we're able to run 'adb devices' successfully, then return
        num_attempts = 10
        backoff_interval = 0.1
        for _ in range(num_attempts):
            exitcode = subprocess.call([self.path, 'devices'], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
            if exitcode == 0:
                return
            time.sleep(backoff_interval)
        
        # If we exceeded our maximum attempts, fail
        raise RuntimeError("Failed to run 'adb devices' successfully after 'adb kill-server && adb start-server'")


    def list_devices(self) -> List[AdbDevice]:
        # Run adb devices
        output, _ = capture_output(self.path, 'devices')
        lines = output.splitlines()

        # First line should be 'List of devices attached'
        head, tail = lines[0], lines[1:]
        if ' devices ' not in head:
            raise RuntimeError(f'Unexpected output from adb devices: {output}')

        # Remaining lines take the format '<device-name>    <status>'
        regex = re.compile(r'([^\s]+)\s+([^\s]+)')
        device_matches = filter(None, [regex.match(line) for line in tail])

        # Status can be 'device' (ready), 'unauthorized', 'offline', etc.: only take
        # devices that are ready
        devices: List[AdbDevice] = []
        for match in device_matches:
            device_name, status = match.group(1), match.group(2)
            if status == 'device':
                devices.append(AdbDevice(self.path, device_name))

        return devices

    def wait_for_device(self, device_name: str, timeout_seconds: float = 30.0) -> AdbDevice:
        args = [self.path, '-s', device_name, 'wait-for-device']
        subprocess.check_call(args, timeout=timeout_seconds)
        return AdbDevice(adb_path=self.path, name=device_name)
    
    def uninstall(self, device_name: str, app_bundle_id: str, timeout_seconds: float = 30.0):
        pm_list_args = [self.path, '-s', device_name, 'shell', 'pm', 'list', 'packages']
        pm_list_output, _ = capture_output(*pm_list_args)
        if f'package:{app_bundle_id}' not in pm_list_output.splitlines():
            return
        args = ['adb', '-s', device_name, 'shell', 'pm', 'uninstall', app_bundle_id]
        subprocess.check_call(args, timeout=timeout_seconds)

    def install(self, device_name: str, app_bundle_path: str, timeout_seconds: float = 60.0):
        args = [self.path, '-s', device_name, 'install', app_bundle_path]
        subprocess.check_call(args, timeout=timeout_seconds)

    def launch(self, device_name: str, app_bundle_id: str, activity_name: str, timeout_seconds: float = 30.0):
        namespaced_activity = f'{app_bundle_id}/{activity_name}'
        args = [self.path, '-s', device_name, 'shell', 'am', 'start', '-n', namespaced_activity]
        subprocess.check_call(args, timeout=timeout_seconds)

    @classmethod
    def require(cls) -> 'Adb':
        path, error_message = resolve_android_binary('platform-tools', 'adb')
        if error_message:
            raise RuntimeError(f'Failed to find adb: {error_message}')
        return cls(path)
