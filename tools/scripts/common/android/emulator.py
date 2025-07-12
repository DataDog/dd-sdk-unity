"""
Utility code for invoking emulator, which simulates Android devices given a target AVD.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
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
