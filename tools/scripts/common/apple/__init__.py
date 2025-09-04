"""
Utility code for interacting with Xcode build tools and iOS simulators.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
from .xcrun import Xcrun, Simctl
from .frontend import AppleDeviceSpec, run_apple_device
from .xcode import run_xcodebuild
from .plutil import get_bundle_identifier
from .libimobiledevice import IDeviceSyslog

__default_ios_device__ = AppleDeviceSpec('iOS 17.4', 'iPhone 15 Pro')


__all__ = [
    '__default_ios_device__',
    'Xcrun',
    'Simctl',
    'AppleDeviceSpec',
    'run_apple_device',
    'run_xcodebuild',
    'get_bundle_identifier',
    'IDeviceSyslog',
]
