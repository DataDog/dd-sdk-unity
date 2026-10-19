# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

from unittest.mock import Mock

import pytest

from dd_sdk_unity.scripts.common.android import adb as module


def test_lists_only_available_devices(monkeypatch):
    output = 'List of devices attached\nphone\tdevice\nemulator-5554\toffline\nlocked\tunauthorized\n'
    monkeypatch.setattr(module.subprocess, 'check_output', lambda *args, **kwargs: output)
    assert module.Adb('/sdk/adb').list_devices() == [module.AdbDevice('/sdk/adb', 'phone')]


def test_boot_poll_waits_between_attempts(monkeypatch):
    output = iter([b'0', b'1'])
    monkeypatch.setattr(module.subprocess, 'check_output', lambda *args, **kwargs: next(output))
    sleep = Mock()
    monkeypatch.setattr(module.time, 'sleep', sleep)
    module.AdbDevice('/sdk/adb', 'emulator-5554').wait_for_boot()
    sleep.assert_called_once_with(0.5)


def test_reverse_port_does_not_claim_an_existing_matching_tunnel(monkeypatch):
    command = Mock(return_value='host-18 tcp:34999 tcp:34999\n')
    monkeypatch.setattr(module.subprocess, 'check_output', command)
    assert not module.AdbDevice('/sdk/adb', 'phone').reverse_port(34999)
    assert command.call_count == 1


def test_reverse_port_does_not_replace_a_conflicting_tunnel(monkeypatch):
    command = Mock(return_value='host-18 tcp:34999 tcp:12345\n')
    monkeypatch.setattr(module.subprocess, 'check_output', command)
    with pytest.raises(RuntimeError, match='already forwards tcp:34999 to tcp:12345'):
        module.AdbDevice('/sdk/adb', 'phone').reverse_port(34999)
    assert command.call_count == 1


def test_reverse_port_creates_a_tunnel_without_rebinding(monkeypatch):
    command = Mock(side_effect=['', b'34999\n'])
    monkeypatch.setattr(module.subprocess, 'check_output', command)
    assert module.AdbDevice('/sdk/adb', 'phone').reverse_port(34999)
    command.assert_called_with(
        ['/sdk/adb', '-s', 'phone', 'reverse', '--no-rebind', 'tcp:34999', 'tcp:34999'],
        timeout=10,
    )


def test_device_wait_survives_an_adb_daemon_restart(monkeypatch):
    command = Mock(side_effect=[module.subprocess.CalledProcessError(1, 'adb'), None])
    monkeypatch.setattr(module.subprocess, 'check_call', command)
    monkeypatch.setattr(module.time, 'sleep', Mock())

    assert module.Adb('/sdk/adb').wait_for_device('emulator-5554') == module.AdbDevice('/sdk/adb', 'emulator-5554')
    assert command.call_count == 2


def test_boot_wait_survives_an_adb_read_failure(monkeypatch):
    command = Mock(side_effect=[module.subprocess.CalledProcessError(1, 'adb'), b'1'])
    monkeypatch.setattr(module.subprocess, 'check_output', command)
    monkeypatch.setattr(module.time, 'sleep', Mock())

    module.AdbDevice('/sdk/adb', 'emulator-5554').wait_for_boot()
    assert command.call_count == 2


def test_device_wait_keeps_its_deadline_while_adb_is_failing(monkeypatch):
    clock = [0.0]
    monkeypatch.setattr(module.time, 'monotonic', lambda: clock[0])
    monkeypatch.setattr(module.time, 'sleep', lambda seconds: clock.__setitem__(0, clock[0] + seconds))
    command = Mock(side_effect=module.subprocess.CalledProcessError(1, 'adb'))
    monkeypatch.setattr(module.subprocess, 'check_call', command)

    with pytest.raises(TimeoutError, match='unavailable after 1'):
        module.Adb('/sdk/adb').wait_for_device('emulator-5554', timeout_seconds=1)
    assert command.call_count == 2


@pytest.mark.parametrize('mapping_present', [False, True])
def test_missing_reverse_mapping_is_already_cleaned_up(monkeypatch, mapping_present):
    error = module.subprocess.CalledProcessError(1, 'adb reverse --remove')
    remaining = 'host-18 tcp:34999 tcp:34999\n' if mapping_present else ''
    command = Mock(side_effect=[error, remaining])
    monkeypatch.setattr(module.subprocess, 'check_output', command)
    device = module.AdbDevice('/sdk/adb', 'phone')

    if mapping_present:
        with pytest.raises(module.subprocess.CalledProcessError) as exception:
            device.remove_reverse_port(34999)
        assert exception.value is error
    else:
        device.remove_reverse_port(34999)
    command.assert_called_with(
        ['/sdk/adb', '-s', 'phone', 'reverse', '--list'], text=True, timeout=10,
    )
