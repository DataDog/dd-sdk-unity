"""
Utility code for invoking emulator, which simulates Android devices given a target AVD.
"""
from dataclasses import dataclass

from .util import resolve_android_binary
    

@dataclass
class AndroidEmulator:
    """
    Wrapper for $ANDROID_HOME/emulator/emulator.
    """
    path: str

    @classmethod
    def require(cls) -> 'AndroidEmulator':
        path, error_message = resolve_android_binary('emulator', 'emulator')
        if error_message:
            raise RuntimeError(f'Failed to find emulator binary: {error_message}')
        return cls(path)
