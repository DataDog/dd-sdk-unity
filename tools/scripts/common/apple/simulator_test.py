"""Regression checks for split test result collection and owned native processes.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import json
from pathlib import Path
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

import pytest

from common.log import init_logger
from common.xslt import transform_nunit_to_junit
from . import simulator


SUITE = '''<test-suite type="TestSuite" name="Integration" testcasecount="2" passed="1" failed="1" skipped="0" duration="1">
<test-case name="passes" classname="Fixture" result="Passed" duration="0.5" asserts="1"/>
<test-case name="fails" classname="Fixture" result="Failed" duration="0.5" asserts="1"><failure><message>native assertion</message><stack-trace>at Fixture</stack-trace></failure></test-case>
</test-suite>'''


def test_runtime_suite_keeps_failures_in_junit(tmp_path):
    source = tmp_path / 'player.xml'
    nunit = tmp_path / 'nunit.xml'
    junit = tmp_path / 'junit.xml'
    source.write_text(SUITE.replace('testcasecount="2"', 'testcasecount="223"'))
    simulator.collect_results(source, nunit)
    transform_nunit_to_junit(str(nunit), str(junit))
    root = ET.parse(junit).getroot()
    assert root.tag == 'testsuites'
    assert root.attrib['tests'] == '2'
    assert root.attrib['failures'] == '1'
    assert root.find('testsuite').attrib['errors'] == '0'
    assert root.find('.//failure').attrib['message'] == 'native assertion'


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


def test_native_timeout_reaps_owned_process(tmp_path, monkeypatch):
    init_logger()
    real_popen = subprocess.Popen
    processes = []

    def popen(*args, **kwargs):
        process = real_popen(*args, **kwargs)
        processes.append(process)
        return process

    monkeypatch.setattr(simulator.subprocess, 'Popen', popen)
    with pytest.raises(RuntimeError, match='timeout'):
        simulator._run([sys.executable, '-c', 'import time; time.sleep(60)'],
                         tmp_path / 'native.log', time.monotonic() + 0.1)
    assert processes[0].poll() is not None


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

    def failed_build(command, log_path, deadline):
        assert command[0] == 'xcodebuild'
        assert command[-1] == 'build'
        assert 'platform=iOS Simulator,id=owned-simulator' in command
        raise RuntimeError('compile failed')

    monkeypatch.setattr(simulator, '_capture', capture)
    monkeypatch.setattr(simulator, '_run', failed_build)
    monkeypatch.setattr(simulator.subprocess, 'run', run)
    with pytest.raises(RuntimeError, match='compile failed'):
        simulator.run_simulator_tests(export, tmp_path / 'nunit.xml', tmp_path / 'native.log')
    assert deleted == [['xcrun', 'simctl', 'shutdown', 'owned-simulator'],
                       ['xcrun', 'simctl', 'delete', 'owned-simulator']]
