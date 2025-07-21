"""
Utility code for interacting with Android build tools and emulators.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
from .sdkmanager import AndroidPackage, AndroidSdkManager
from .avdmanager import AvdManager
from .emulator import AndroidEmulator
from .adb import Adb

from .frontend import AndroidDeviceSpec, run_android_device


__all__ = [
    'AndroidPackage',
    'AndroidSdkManager',
    'AvdManager',
    'AndroidEmulator',
    'Adb',
    'AndroidDeviceSpec',
    'run_android_device',
]
