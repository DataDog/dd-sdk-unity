"""
Utility code for modifying a Unity project's ProjectSettings.asset file in order to
control whether the generate Xcode project targets iOS simulators or physical iOS
devices.
"""
import os
import platform as _platform
from contextlib import contextmanager
from enum import Enum
from typing import Optional


__project_settings_asset_filename__ = 'ProjectSettings.asset'
__project_settings_asset_relpath__ = os.path.join('ProjectSettings', __project_settings_asset_filename__)


class TargetIosSdk(Enum):
    DEVICE = 988
    SIMULATOR = 989


class TargetIosSimulatorArchitecture(Enum):
    X86_64 = 0
    ARM64 = 1
    UNIVERSAL = 2


@contextmanager
def modified_ios_target_settings(project_root: str, platform: str, target: str):
    # If we're launching on non-iOS platforms, do nothing
    if platform != 'ios':
        yield
        return
    
    # Determine what settings to use based on whether we're targeting a simulator or device
    sdk = TargetIosSdk.DEVICE
    architecture: Optional[TargetIosSimulatorArchitecture] = None
    if target != 'device':
        assert target == 'simulator'
        sdk = TargetIosSdk.SIMULATOR
        architecture = TargetIosSimulatorArchitecture.ARM64 if _platform.processor() == 'arm' else TargetIosSimulatorArchitecture.X86_64

    # Read the original contents of ProjectSettings.asset
    path = os.path.join(project_root, __project_settings_asset_relpath__)
    with open(path) as fp:
        old_text = fp.read()

    # Modify the file to contain our desired settings
    new_text = _modify_project_settings_impl(old_text, sdk, architecture)
    with open(path, 'w') as fp:
        fp.write(new_text)

    # Yield, then ensure that we revert to the original file contents
    try:
        yield
    finally:
        with open(path, 'w') as fp:
            fp.write(old_text)


def _modify_project_settings_impl(text: str, sdk: TargetIosSdk, architecture: Optional[TargetIosSimulatorArchitecture]) -> str:
    lines = text.splitlines()
    to_modify = [
        ('iPhoneSdkVersion', sdk.value),
        ('iOSSimulatorArchitecture', architecture.value if architecture is not None else None),
    ]
    for key, value_or_none in to_modify:
        if value_or_none is None:
            continue
        value = value_or_none
        prefix = f'  {key}:'
        i = next((i for i, s in enumerate(lines) if s.startswith(prefix)), -1)
        if i < 0:
            raise ValueError(f"Invalid {__project_settings_asset_filename__} file: no existing line begins with {prefix}")
        lines[i] = f'{prefix} {value}'
    return '\n'.join(lines) + '\n'
