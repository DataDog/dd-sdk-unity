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
from .aapt import get_package_name

from .frontend import AndroidDeviceSpec, run_android_device

__default_android_device__ = AndroidDeviceSpec.default(api_level=33, device='pixel_4')

__all__ = [
    '__default_android_device__',
    'AndroidPackage',
    'AndroidSdkManager',
    'AvdManager',
    'AndroidEmulator',
    'Adb',
    'get_package_name',
    'AndroidDeviceSpec',
    'run_android_device',
]
