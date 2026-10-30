# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

from contextlib import contextmanager
from unittest.mock import Mock

import pytest

from dd_sdk_unity.scripts import start_simulator as module


def test_connected_device_is_reused_without_claiming_it(monkeypatch, capsys):
    device = Mock()
    device.name = 'phone'
    adb = Mock()
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    adb.list_devices.return_value = [device]
    adb.wait_for_device.return_value = device
    launch = Mock(side_effect=AssertionError('Must reuse the connected device'))
    monkeypatch.setattr(module, 'run_default_simulator', launch)
    module.start_android_simulator()
    device.wait_for_boot.assert_called_once_with(timeout_seconds=120)
    adb.wait_for_device.assert_called_once_with('phone', timeout_seconds=120)
    assert capsys.readouterr().out == ''
    launch.assert_not_called()


def test_start_returns_only_the_new_emulator_serial(monkeypatch, capsys):
    adb = Mock(path='/sdk/adb')
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    adb.list_devices.return_value = []
    device = Mock()
    device.name = 'emulator-5594'
    @contextmanager
    def start(platform, *, keep_running, on_started):
        assert platform == 'android'
        assert keep_running
        on_started(device.name)
        yield device
    monkeypatch.setattr(module, 'run_default_simulator', start)
    module.start_android_simulator()
    assert capsys.readouterr().out == 'emulator-5594\n'


def test_stop_targets_the_owned_console_without_waiting_for_adb(monkeypatch):
    adb = Mock(path='/sdk/adb')
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    kill = Mock()
    monkeypatch.setattr(module.AdbDevice, 'emu_kill', kill)

    module.stop_android_simulator('emulator-5594')
    kill.assert_called_once()
    adb.list_devices.assert_not_called()


def test_stopping_a_physical_device_is_harmless(monkeypatch):
    kill = Mock()
    monkeypatch.setattr(module.AdbDevice, 'emu_kill', kill)
    module.stop_android_simulator('phone')
    kill.assert_not_called()


@pytest.mark.parametrize('status', [None, 'offline', 'device'])
def test_stop_keeps_failures_for_an_emulator_still_present(monkeypatch, status):
    adb = Mock(path='/sdk/adb')
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    error = module.subprocess.CalledProcessError(1, 'adb emu kill')
    monkeypatch.setattr(module.AdbDevice, 'emu_kill', Mock(side_effect=error))
    output = 'List of devices attached\n'
    if status:
        output += f'emulator-5594\t{status}\n'
    monkeypatch.setattr(module.subprocess, 'check_output', Mock(return_value=output))

    if status:
        with pytest.raises(module.subprocess.CalledProcessError) as exception:
            module.stop_android_simulator('emulator-5594')
        assert exception.value is error
    else:
        module.stop_android_simulator('emulator-5594')


def test_editor_connection_covers_all_devices_and_returns_only_new_mappings(monkeypatch, capsys):
    existing, phone, emulator = Mock(), Mock(), Mock()
    existing.name, phone.name, emulator.name = 'existing', 'phone', 'emulator-5554'
    existing.reverse_port.return_value = False
    phone.reverse_port.side_effect = [True, False]
    emulator.reverse_port.side_effect = [True, False]
    adb = Mock()
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    adb.list_devices.return_value = [existing, phone, emulator]

    module.connect_android_editor(34999)
    assert capsys.readouterr().out == 'phone\nemulator-5554\n'
    for device in (existing, phone, emulator):
        device.reverse_port.assert_called_once_with(34999)
        device.remove_reverse_port.assert_not_called()

    module.connect_android_editor(34999)
    assert capsys.readouterr().out == ''


def test_editor_connection_failure_rolls_back_only_new_mappings(monkeypatch, capsys):
    first, existing, second, failed = Mock(), Mock(), Mock(), Mock()
    first.name, existing.name, second.name, failed.name = 'phone', 'existing', 'emulator-5554', 'failed'
    first.reverse_port.return_value = True
    existing.reverse_port.return_value = False
    second.reverse_port.return_value = True
    failed.reverse_port.side_effect = RuntimeError('connection failed')
    adb = Mock()
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    adb.list_devices.return_value = [first, existing, second, failed]

    with pytest.raises(RuntimeError, match='connection failed'):
        module.connect_android_editor(34999)

    first.remove_reverse_port.assert_called_once_with(34999)
    second.remove_reverse_port.assert_called_once_with(34999)
    existing.remove_reverse_port.assert_not_called()
    failed.remove_reverse_port.assert_not_called()
    assert capsys.readouterr().out == ''


def test_editor_disconnect_only_targets_the_tracked_device(monkeypatch):
    owned, other = Mock(), Mock()
    owned.name, other.name = 'phone', 'emulator-5554'
    adb = Mock()
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    adb.list_devices.return_value = [other, owned]
    module.disconnect_android_editor('phone', 34999)
    owned.remove_reverse_port.assert_called_once_with(34999)
    other.remove_reverse_port.assert_not_called()


def test_editor_connection_requires_an_available_device(monkeypatch):
    adb = Mock()
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    adb.list_devices.return_value = []
    with pytest.raises(RuntimeError, match='No Android device'):
        module.connect_android_editor(34999)


def test_start_reports_owned_serial_even_when_boot_fails(monkeypatch, capsys):
    adb = Mock()
    adb.list_devices.return_value = []
    monkeypatch.setattr(module.Adb, 'require', lambda: adb)
    @contextmanager
    def fail_boot(platform, *, keep_running, on_started):
        on_started('emulator-5594')
        raise TimeoutError('boot failed')
        yield
    monkeypatch.setattr(module, 'run_default_simulator', fail_boot)

    with pytest.raises(TimeoutError, match='boot failed'):
        module.start_android_simulator()
    assert capsys.readouterr().out == 'emulator-5594\n'
