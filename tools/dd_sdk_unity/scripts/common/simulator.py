# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code providing a platform-agnostic abstraction layer for running emulated iOS
or Android devices.
"""
from collections.abc import Generator
from contextlib import contextmanager

from dd_sdk_unity.scripts.common import MobilePlatform
from dd_sdk_unity.scripts.common.android import AndroidDeviceSpec, run_android_device
from dd_sdk_unity.scripts.common.apple import AppleDeviceSpec, run_apple_device

__default_ios_device__ = AppleDeviceSpec('iOS 18.5', 'iPhone 15 Pro')
__default_android_device__ = AndroidDeviceSpec.default(api_level=33, device='pixel_4')


@contextmanager
def run_default_simulator(platform: MobilePlatform) -> Generator[None]:
    if platform.lower() == 'ios':
        with run_apple_device(__default_ios_device__):
            yield
    else:
        assert platform.lower() == 'android'
        with run_android_device(__default_android_device__):
            yield
