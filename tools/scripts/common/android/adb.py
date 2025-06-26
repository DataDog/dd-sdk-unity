import time
import subprocess

from dataclasses import dataclass

from .util import resolve_android_binary


@dataclass
class AdbDevice:
    adb_path: str
    name: str

    def wait_for_boot(self, timeout_seconds: float = 30.0, interval_seconds = 0.5):
        started_at = time.time()
        while True:
            args = [self.adb_path, '-s', self.name, 'shell', 'getprop', 'sys.boot_completed']
            output = subprocess.check_output(args, timeout=1.0)
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

    def wait_for_device(self, device_name: str, timeout_seconds: float = 30.0) -> AdbDevice:
        args = [self.path, '-s', device_name, 'wait-for-device']
        subprocess.check_call(args, timeout=timeout_seconds)
        return AdbDevice(adb_path=self.path, name=device_name)

    @classmethod
    def require(cls) -> 'Adb':
        path, error_message = resolve_android_binary('platform-tools', 'adb')
        if error_message:
            raise RuntimeError(f'Failed to find adb: {error_message}')
        return cls(path)
