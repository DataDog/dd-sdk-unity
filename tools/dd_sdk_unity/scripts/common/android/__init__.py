# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for interacting with Android build tools and emulators.
"""
from dd_sdk_unity.scripts.common.android.adb import Adb
from dd_sdk_unity.scripts.common.android.avdmanager import AvdManager
from dd_sdk_unity.scripts.common.android.emulator import AndroidEmulator
from dd_sdk_unity.scripts.common.android.frontend import AndroidDeviceSpec, run_android_device
from dd_sdk_unity.scripts.common.android.sdkmanager import AndroidPackage, AndroidSdkManager

__all__ = [
    'Adb',
    'AndroidDeviceSpec',
    'AndroidEmulator',
    'AndroidPackage',
    'AndroidSdkManager',
    'AvdManager',
    'run_android_device',
]
