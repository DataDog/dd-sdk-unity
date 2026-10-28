# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Common library code used by dd-sdk-unity Python scripts.
"""
from typing import Literal

type MobilePlatform = Literal['ios', 'android']
type TargetPlatform = MobilePlatform | Literal['webgl']
type DeviceTarget = Literal['simulator', 'device']
type TestMode = Literal['EditMode', 'PlayMode']
