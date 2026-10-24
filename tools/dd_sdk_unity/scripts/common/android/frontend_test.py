# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

import os
from pathlib import Path
from types import SimpleNamespace
from typing import cast
from unittest.mock import Mock, call

import pytest

from dd_sdk_unity.scripts.common.android import frontend as module


@pytest.fixture
def emulator(monkeypatch):
    spec = module.AndroidDeviceSpec.default(api_level=33, device='pixel_4')
    sdk = Mock()
    sdk.list_installed_packages.return_value = [
        SimpleNamespace(path='emulator'), SimpleNamespace(path=spec.system_image_package),
    ]
    avd = Mock()
    avd.list_avds.return_value = [SimpleNamespace(
        api_level=spec.api_level, device=spec.device, tag=spec.tag, abi=spec.abi, name='test-avd')]
    monkeypatch.setattr(module.AndroidSdkManager, 'require', lambda: sdk)
    monkeypatch.setattr(module.AvdManager, 'require', lambda: avd)
    monkeypatch.setattr(module.AndroidEmulator, 'require', lambda: SimpleNamespace(path='/sdk/emulator'))
    process = Mock()
    launch = Mock(return_value=process)
    monkeypatch.setattr(module.subprocess, 'Popen', launch)
    cleanup = Mock()
    monkeypatch.setattr(module, '_stop_android_device', cleanup)
    adb = Mock()
    adb.path = '/host-sdk/platform-tools/adb'
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    return spec, adb, process, launch, cleanup


@pytest.mark.parametrize('keep_running', [False, True])
def test_detaches_only_after_successful_start(emulator, keep_running):
    spec, adb, process, launch, cleanup = emulator
    original_path = os.environ.get('PATH')
    with module.run_android_device(spec, keep_running=keep_running) as device:
        assert device is adb.wait_for_device.return_value
    assert launch.call_args.kwargs['start_new_session'] is keep_running
    assert '-wipe-data' in launch.call_args.args[0]
    arguments = launch.call_args.args[0]
    assert arguments[arguments.index('-adb-path') + 1] == adb.path
    assert launch.call_args.kwargs['env']['PATH'].split(os.pathsep)[0] == str(Path(adb.path).parent)
    assert os.environ.get('PATH') == original_path
    if keep_running:
        cleanup.assert_not_called()
    else:
        cleanup.assert_called_once_with(process, device)


def test_detached_start_still_cleans_up_after_boot_failure(emulator):
    spec, adb, process, _launch, cleanup = emulator
    device = adb.wait_for_device.return_value
    started = []
    def fail_boot(**kwargs):
        assert started == [adb.wait_for_device.call_args.args[0]]
        raise TimeoutError('boot failed')
    device.wait_for_boot.side_effect = fail_boot
    with (pytest.raises(TimeoutError, match='boot failed'),
          module.run_android_device(spec, keep_running=True, on_started=started.append)):
        pytest.fail('Must not yield a device that failed to boot')
    cleanup.assert_called_once_with(process, device)


def test_detached_start_cleans_up_when_the_caller_fails(emulator):
    spec, _adb, process, _launch, cleanup = emulator
    with (pytest.raises(RuntimeError, match='record failed'),
          module.run_android_device(spec, keep_running=True) as device):
        raise RuntimeError('record failed')
    cleanup.assert_called_once_with(process, device)


@pytest.mark.parametrize('image_installed', [False, True])
def test_only_installs_the_missing_system_image(emulator, image_installed):
    spec, _adb, _process, _launch, _cleanup = emulator
    sdk = cast(Mock, module.AndroidSdkManager.require())
    # Tools can be available without the emulator appearing in package metadata.
    sdk.list_installed_packages.return_value = (
        [SimpleNamespace(path=spec.system_image_package)] if image_installed else []
    )
    with module.run_android_device(spec):
        pass
    assert sdk.install_package.call_args_list == (
        [] if image_installed else [call(spec.system_image_package)]
    )


def test_missing_emulator_fails_before_installing_packages(emulator, monkeypatch):
    spec, _adb, _process, launch, _cleanup = emulator
    sdk = cast(Mock, module.AndroidSdkManager.require())
    missing = Mock(side_effect=RuntimeError('emulator is not installed'))
    monkeypatch.setattr(module.AndroidEmulator, 'require', missing)

    with (pytest.raises(RuntimeError, match='emulator is not installed'),
          module.run_android_device(spec)):
        pytest.fail('Must not yield a device without emulator tooling')

    sdk.install_package.assert_not_called()
    sdk.list_installed_packages.assert_not_called()
    launch.assert_not_called()


def test_package_install_failure_does_not_launch_emulator(emulator, monkeypatch):
    spec, _adb, _process, launch, _cleanup = emulator
    sdk = cast(Mock, module.AndroidSdkManager.require())
    sdk.list_installed_packages.return_value = []
    sdk.install_package.side_effect = PermissionError('SDK is read-only')
    with (pytest.raises(PermissionError, match='SDK is read-only'),
          module.run_android_device(spec)):
        pytest.fail('Must not yield a device after package installation fails')

    launch.assert_not_called()
