# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for interacting with Xcode build tools and iOS simulators.
"""
from dd_sdk_unity.scripts.common.apple.frontend import AppleDeviceSpec, run_apple_device
from dd_sdk_unity.scripts.common.apple.xcode import run_xcodebuild
from dd_sdk_unity.scripts.common.apple.xcrun import Simctl, Xcrun

__all__ = [
    'AppleDeviceSpec',
    'Simctl',
    'Xcrun',
    'run_apple_device',
    'run_xcodebuild',
]
