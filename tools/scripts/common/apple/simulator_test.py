"""Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

Regression checks for split test result collection and owned native processes.
"""
import json
from pathlib import Path
import plistlib
import subprocess
import xml.etree.ElementTree as ET

import pytest

from common.log import init_logger
from . import simulator


FIXTURES = Path(__file__).with_name('fixtures')


def test_collect_results_matches_expected_nunit(tmp_path):
    result = tmp_path / 'nunit.xml'
    simulator.collect_results(FIXTURES / 'runtime-suite-given.xml', result)
    expected = FIXTURES / 'runtime-suite-expected.xml'
    assert ET.canonicalize(from_file=result, strip_text=True) == ET.canonicalize(from_file=expected, strip_text=True)


@pytest.mark.parametrize('text', ['<test-suite/>', '<unexpected/>', '<test-suite>'])
def test_empty_wrong_or_partial_result_is_rejected(tmp_path, text):
    source = tmp_path / 'player.xml'
    source.write_text(text)
    with pytest.raises((RuntimeError, ET.ParseError)):
        simulator.collect_results(source, tmp_path / 'nunit.xml')


def test_simulator_selection_uses_available_runtime_not_fixed_version():
    runtimes = [dict(identifier='com.apple.CoreSimulator.SimRuntime.iOS-18-5', version='18.5', isAvailable=True),
                dict(identifier='com.apple.CoreSimulator.SimRuntime.iOS-26-0', version='26.0', isAvailable=True),
                dict(identifier='com.apple.CoreSimulator.SimRuntime.iOS-27-0', version='27.0', isAvailable=False)]
    devices = {runtime['identifier']: [dict(isAvailable=True,
                deviceTypeIdentifier='com.apple.CoreSimulator.SimDeviceType.iPhone-17')] for runtime in runtimes}
    assert simulator._simulator_type(runtimes, devices)[0].endswith('iOS-26-0')


@pytest.mark.parametrize('shutdown_fails', [False, True])
def test_compile_failure_deletes_only_owned_simulator(tmp_path, monkeypatch, shutdown_fails):
    init_logger()
    export = tmp_path / 'export'
    (export / 'Unity-iPhone.xcodeproj').mkdir(parents=True)
    deleted = []

    def capture(*command, **kwargs):
        if command[2:4] == ('list', 'runtimes'):
            return json.dumps({'runtimes': [dict(identifier='com.apple.CoreSimulator.SimRuntime.iOS-26-0', version='26.0', isAvailable=True)]})
        if command[2:4] == ('list', 'devices'):
            return json.dumps({'devices': {'com.apple.CoreSimulator.SimRuntime.iOS-26-0': [dict(isAvailable=True, deviceTypeIdentifier='iPhone')]}})
        if command[2] == 'create':
            return 'owned-simulator'
        pytest.fail(str(command))

    def run(command, **kwargs):
        deleted.append(command)
        if shutdown_fails and command[2] == 'shutdown':
            raise subprocess.TimeoutExpired(command, 30)
        return subprocess.CompletedProcess(command, 0)

    def failed_build(cwd, command):
        assert cwd == str(export)
        assert command[-1] == 'build'
        assert 'platform=iOS Simulator,id=owned-simulator' in command
        raise RuntimeError('compile failed')

    monkeypatch.setattr(simulator, '_capture', capture)
    monkeypatch.setattr(simulator, 'run_xcodebuild', failed_build)
    monkeypatch.setattr(simulator.subprocess, 'run', run)
    with pytest.raises(RuntimeError, match='compile failed'):
        simulator.run_simulator_tests(export, tmp_path / 'nunit.xml', tmp_path / 'native.log')
    assert deleted == [['xcrun', 'simctl', 'shutdown', 'owned-simulator'],
                       ['xcrun', 'simctl', 'delete', 'owned-simulator']]


def test_hung_player_times_out_and_cleans_owned_simulator(tmp_path, monkeypatch):
    init_logger()
    export = tmp_path / 'export'
    (export / 'Unity-iPhone.xcodeproj').mkdir(parents=True)
    app = tmp_path / 'ios-derived-data/Build/Products/Debug-iphonesimulator/Tests.app'
    app.mkdir(parents=True)
    (app / 'Info.plist').write_bytes(plistlib.dumps({'CFBundleIdentifier': 'test.bundle'}))
    container = tmp_path / 'container'
    container.mkdir()
    cleanup = []
    elapsed = 0

    def capture(*command):
        if command[2:4] == ('list', 'runtimes'):
            return json.dumps({'runtimes': [dict(identifier='com.apple.CoreSimulator.SimRuntime.iOS-26-0',
                                                version='26.0', isAvailable=True)]})
        if command[2:4] == ('list', 'devices'):
            return json.dumps({'devices': {'com.apple.CoreSimulator.SimRuntime.iOS-26-0': [
                dict(isAvailable=True, deviceTypeIdentifier='iPhone')]}})
        if command[2] == 'create':
            return 'owned-simulator'
        if command[2] == 'get_app_container':
            return str(container)
        pytest.fail(str(command))

    class HungPlayer:
        returncode = None

        def poll(self):
            return None

    player = HungPlayer()

    def sleep(seconds):
        nonlocal elapsed
        elapsed += 60

    def run(command, **kwargs):
        cleanup.append(command)
        return subprocess.CompletedProcess(command, 0)

    monkeypatch.setattr(simulator, '_capture', capture)
    monkeypatch.setattr(simulator, 'run_xcodebuild', lambda *args: None)
    monkeypatch.setattr(simulator, '_run', lambda *args: None)
    monkeypatch.setattr(simulator.subprocess, 'Popen', lambda *args, **kwargs: player)
    monkeypatch.setattr(simulator.subprocess, 'run', run)
    monkeypatch.setattr(simulator, 'stop_process', lambda process: cleanup.append(process))
    monkeypatch.setattr(simulator.time, 'monotonic', lambda: elapsed)
    monkeypatch.setattr(simulator.time, 'sleep', sleep)

    with pytest.raises(RuntimeError, match='did not write NUnit results within 10 minutes'):
        simulator.run_simulator_tests(export, tmp_path / 'nunit.xml', tmp_path / 'native.log')

    assert elapsed == 600
    assert cleanup == [['xcrun', 'simctl', 'terminate', 'owned-simulator', 'test.bundle'], player,
                       ['xcrun', 'simctl', 'shutdown', 'owned-simulator'],
                       ['xcrun', 'simctl', 'delete', 'owned-simulator']]
    assert not (tmp_path / 'nunit.xml').exists()
