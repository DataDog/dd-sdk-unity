"""Regression checks for split test result collection and owned native processes.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import json
from pathlib import Path
import plistlib
import subprocess
import xml.etree.ElementTree as ET

import pytest

from common.log import init_logger
from common.xslt import transform_nunit_to_junit
from junitparser import JUnitXml
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


@pytest.mark.parametrize('site,label,expected_name,expected_element', [
    ('SetUp', 'Error', 'initializationError', 'error'),
    ('TearDown', '', 'executionError', 'failure'),
    ('TearDown', 'Error', 'executionError', 'error'),
])
@pytest.mark.parametrize('has_case', [False, True])
@pytest.mark.parametrize('propagated_site', ['Child', 'Parent'])
def test_suite_failure_becomes_a_junit_case(tmp_path, site, label, expected_name, expected_element, has_case, propagated_site):
    source = tmp_path / 'nunit.xml'
    junit = tmp_path / 'junit.xml'
    case = '<test-case name="passes" classname="Fixture" result="Passed" duration="0"/>' if has_case else ''
    count = int(has_case)
    source.write_text(f'''<test-run testcasecount="{count}" failed="0" skipped="0" duration="1">
<test-suite type="Assembly" name="Tests" testcasecount="{count}" passed="{count}" failed="0" skipped="0" duration="1" site="{propagated_site}">
<failure><message>Child fixture failed</message></failure>
<test-suite type="TestFixture" name="Fixture" fullname="Tests.Fixture" testcasecount="{count}" passed="{count}" failed="0" skipped="0" duration="1" result="Failed" site="{site}" label="{label}">
{case}<failure><message>fixture exploded</message><stack-trace>at Fixture.Hook</stack-trace></failure>
</test-suite></test-suite></test-run>''')
    transform_nunit_to_junit(str(source), str(junit))
    root = ET.parse(junit).getroot()
    cases = root.findall('.//testcase')
    assert len(cases) == count + 1
    assert root.attrib['tests'] == str(count + 1)
    synthetic = cases[-1]
    assert synthetic.attrib['name'] == expected_name
    assert synthetic.attrib['classname'] == 'Tests.Fixture'
    failure = synthetic.find(expected_element)
    assert failure is not None
    assert failure.attrib['message'] == 'fixture exploded'
    assert failure.text == 'fixture exploded\nat Fixture.Hook'
    assert sum(not case.is_passed for suite in JUnitXml.fromfile(junit) for case in suite) == 1


def test_nested_setup_failure_counts_only_emitted_cases(tmp_path):
    source = tmp_path / 'nunit.xml'
    junit = tmp_path / 'junit.xml'
    source.write_text('''<test-run testcasecount="1" failed="1" skipped="0" duration="1">
<test-suite name="Fixture" fullname="Tests.Fixture" testcasecount="1" failed="1" duration="1" site="SetUp" label="Error">
<failure><message>setup exploded</message><stack-trace>at Fixture.Setup</stack-trace></failure>
<test-suite name="Parameterized" testcasecount="1" failed="1" duration="1" site="Parent">
<failure><message>setup exploded</message></failure>
<test-case name="blocked(1)" classname="Tests.Fixture" result="Failed" duration="0">
<failure><message>OneTimeSetUp: setup exploded</message></failure>
</test-case></test-suite></test-suite></test-run>''')
    transform_nunit_to_junit(str(source), str(junit))
    root = ET.parse(junit).getroot()
    assert root.attrib['tests'] == '2'
    assert len(root.findall('.//testcase')) == 2
    assert len(root.findall('.//testcase[@name="initializationError"]')) == 1
    for suite in root.findall('testsuite'):
        assert suite.attrib['tests'] == str(len(suite.findall('testcase')))
        assert suite.attrib['failures'] == str(len(suite.findall('testcase/failure')))
        assert suite.attrib['errors'] == str(len(suite.findall('testcase/error')))


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
