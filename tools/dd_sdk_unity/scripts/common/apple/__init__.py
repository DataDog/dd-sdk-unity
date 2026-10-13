"""
Utility code for interacting with Xcode build tools and iOS simulators.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
from dd_sdk_unity.scripts.common.apple.xcrun import Xcrun, Simctl
from dd_sdk_unity.scripts.common.apple.frontend import AppleDeviceSpec, run_apple_device
from dd_sdk_unity.scripts.common.apple.xcode import run_xcodebuild


__all__ = [
    'Xcrun',
    'Simctl',
    'AppleDeviceSpec',
    'run_apple_device',
    'run_xcodebuild',
]
