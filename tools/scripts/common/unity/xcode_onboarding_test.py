"""
Unless explicitly stated otherwise, licensed under Apache License Version 2.0.
Copyright 2026-Present Datadog, Inc.
"""
import json
import os
from pathlib import Path
import plistlib
import subprocess
import sys

import pytest

from . import xcode_onboarding as onboarding


@pytest.fixture
def probe(tmp_path, monkeypatch):
    state = {'pids': {42}, 'chooser': True, 'calls': [], 'prefs_calls': 0, 'error': None, 'videos': []}
    messages = []

    def capture(command, output, timeout=10):
        state['calls'].append(command)
        if command[0] == '/usr/bin/xcode-select':
            output.write_text('/Applications/Xcode-26.0.0.app/Contents/Developer\n')
        elif command[0] == '/bin/ps':
            output.write_text(''.join(f'{pid} /Applications/Xcode-26.0.0.app/Contents/MacOS/Xcode\n'
                                     for pid in state['pids']) + '99 /Applications/Other.app/Contents/MacOS/Other\n')
        else:
            assert command[0] == '/usr/bin/osascript'
            assert command[-2] == '43'
            if state['error']:
                output.write_text(state['error'])
                return 1
            mode = command[-1]
            result = {'chooser': state['chooser'], 'windows': []} if mode == 'probe' else {
                'outcome': 'deselected' if mode == 'deselect' else 'submitted'}
            if mode == 'continue':
                state['chooser'] = False
            output.write_text(json.dumps(result))
        return 0

    def preferences(command, stdout, stderr, timeout):
        assert command == ['/usr/bin/defaults', 'export', 'com.apple.dt.Xcode', '-']
        state['prefs_calls'] += 1
        stdout.write(plistlib.dumps({
            'OnboardingCompleted': state['prefs_calls'] > 1,
            'Account': {'AccessToken': 'do-not-publish'},
        }))
        return subprocess.CompletedProcess(command, 0)

    monkeypatch.setattr(onboarding, 'run_capture', capture)
    monkeypatch.setattr(onboarding.subprocess, 'run', preferences)
    clock = [0]
    monkeypatch.setattr(onboarding.time, 'monotonic', lambda: clock[0])
    runner = onboarding.XcodeOnboardingProbe(tmp_path, messages.append, state['videos'].append)
    state['pids'] = {42, 43}
    return runner, state, clock, messages


def test_only_new_selected_xcode_is_probed_and_preferences_compared(probe):
    runner, state, clock, messages = probe
    runner.poll()
    clock[0] = 5
    runner.poll()
    clock[0] = 10
    runner.poll()
    assert runner.done
    assert state['videos'] == ['xcode-onboarding']
    assert [c[-1] for c in state['calls'] if c[0] == '/usr/bin/osascript'] == [
        'probe', 'deselect', 'continue', 'probe']
    changes = json.loads((runner.directory / 'xcode-preferences-diff.json').read_text())
    assert changes == {'OnboardingCompleted': {
        'before': False, 'after': True, 'before_present': True, 'after_present': True}}
    assert not any('do-not-publish' in p.read_text() for p in runner.directory.glob('*.json'))
    assert any('component chooser disappeared' in m for m in messages)
    count = len(state['calls'])
    runner.poll()
    runner.close()
    assert len(state['calls']) == count and state['prefs_calls'] == 2


def test_permission_denied_stops_without_clicks(probe):
    runner, state, clock, messages = probe
    state['error'] = 'osascript is not allowed assistive access (-1719)'
    runner.poll()
    assert runner.done
    assert [c[-1] for c in state['calls'] if c[0] == '/usr/bin/osascript'] == ['probe']
    assert 'assistive access' in (runner.directory / 'xcode-ui-probe-01.json').read_text()
    assert (runner.directory / 'xcode-preferences-diff.json').exists()
    assert any('unavailable' in m for m in messages)


def test_existing_or_ambiguous_xcode_is_not_inspected(probe):
    runner, state, clock, messages = probe
    state['pids'] = {42}
    runner.poll()
    assert not any(c[0] == '/usr/bin/osascript' for c in state['calls'])
    state['pids'] = {42, 43, 44}
    clock[0] = 30
    runner.poll()
    assert runner.done
    assert not any(c[0] == '/usr/bin/osascript' for c in state['calls'])


def test_unrecognized_windows_never_trigger_action(probe):
    runner, state, clock, messages = probe
    state['chooser'] = False
    for seconds in (0, 5, 10):
        clock[0] = seconds
        runner.poll()
    assert runner.done
    assert [c[-1] for c in state['calls'] if c[0] == '/usr/bin/osascript'] == ['probe'] * 3


def test_attempt_deadline_stops_future_ui_commands(probe):
    runner, state, clock, messages = probe
    runner.poll()
    clock[0] = onboarding.ATTEMPT_SECONDS
    runner.poll()
    assert runner.done
    assert len([c for c in state['calls'] if c[0] == '/usr/bin/osascript']) == 2
    assert any('deadline exceeded' in m for m in messages)


def test_timeout_reaps_real_owned_child(tmp_path, monkeypatch):
    children = []
    real_popen = subprocess.Popen

    def popen(*args, **kwargs):
        child = real_popen(*args, **kwargs)
        children.append(child)
        return child
    monkeypatch.setattr(onboarding.subprocess, 'Popen', popen)
    result = onboarding.run_capture(
        [sys.executable, '-c', 'import threading; threading.Event().wait()'],
        tmp_path / 'timeout.log', timeout=0.1)
    assert result is None
    assert children[0].poll() is not None
    with pytest.raises(ProcessLookupError):
        os.kill(children[0].pid, 0)
    assert 'timed out' in (tmp_path / 'timeout.log').read_text()


def test_cancellation_reaps_real_owned_child(tmp_path, monkeypatch):
    children = []
    real_popen = subprocess.Popen

    def popen(*args, **kwargs):
        child = real_popen(*args, **kwargs)
        children.append(child)
        real_wait = child.wait
        calls = []

        def wait(timeout):
            if not calls:
                calls.append(True)
                raise KeyboardInterrupt
            return real_wait(timeout=timeout)
        child.wait = wait
        return child
    monkeypatch.setattr(onboarding.subprocess, 'Popen', popen)
    with pytest.raises(KeyboardInterrupt):
        onboarding.run_capture(
            [sys.executable, '-c', 'import threading; threading.Event().wait()'],
            tmp_path / 'cancel.log', timeout=1)
    assert children[0].poll() is not None
    with pytest.raises(ProcessLookupError):
        os.kill(children[0].pid, 0)


@pytest.mark.parametrize('case,mode,outcome,clicks', [
    ('normal', 'probe', None, 0),
    ('normal', 'deselect', 'deselected', 2),
    ('cleared', 'continue', 'submitted', 1),
    ('normal', 'continue', 'downloads-still-selected', 0),
    ('unknown_checkbox', 'deselect', 'unrecognized-checkbox', 0),
    ('other_window', 'deselect', 'unrecognized-window', 0),
    ('truncated', 'deselect', 'unrecognized-window', 0),
    ('deep', 'continue', 'unrecognized-window', 0),
    ('unknown_button', 'continue', 'unrecognized-continue-button', 0),
    ('uncertain_checkbox', 'continue', 'unrecognized-checkbox-state', 0),
    ('no_checkboxes', 'continue', 'unrecognized-checkbox-state', 0),
    ('metadata_checkbox', 'deselect', 'deselected', 2),
    ('title_element_checkbox', 'deselect', 'deselected', 2),
    ('metadata_button', 'continue', 'submitted', 1),
    ('metadata_unknown_checkbox', 'deselect', 'unrecognized-checkbox', 0),
    ('metadata_wrong_button', 'continue', 'unrecognized-continue-button', 0),
    ('metadata_snapshot', 'probe', None, 0),
    ('real_unnamed_probe', 'probe', None, 0),
    ('real_unnamed_deselect', 'deselect', 'unrecognized-checkbox', 0),
])
def test_jxa_actions_are_scoped_to_recognized_controls(case, mode, outcome, clicks):
    # Execute the actual JS against a fake System Events API; no macOS UI access.
    script = Path(onboarding.__file__).with_suffix('.js')
    harness = r"""
const vm = require('vm'), fs = require('fs'), assert = require('assert');
const script = process.argv[1], scenario = process.argv[2], mode = process.argv[3];
const calls = [];
function element(role, name, value, children=[], enabled=true) {
    const e = {
        role: () => role, name: () => typeof name === 'function' ? name() : name,
        description: () => '', value: () => e.current,
        enabled: () => enabled, uiElements: () => children, current: value,
        metadata: {}, position: () => [100, 200], size: () => [30, 40]
    };
    e.attributes = {
        name: () => Object.keys(e.metadata),
        byName: key => ({value: () => {
            if (!(key in e.metadata)) throw new Error('Attribute unavailable');
            return e.metadata[key];
        }})
    };
    return e;
}
const checked = scenario === 'cleared' || scenario === 'deep' || ['unknown_button','metadata_button','metadata_wrong_button'].includes(scenario) ? 0 : 1;
const ios = element('AXCheckBox', scenario === 'unknown_checkbox' ? 'Unknown download' : 'iOS 26.0', checked);
const model = element('AXCheckBox', 'Predictive Code Completion Model', checked);
const mac = element('AXCheckBox', 'macOS 26.0', 1, [], false);
const button = element('AXButton', () => scenario === 'unknown_button' ? 'Delete Project' :
    (ios.current || model.current ? 'Download & Install' : 'Continue'), null);
const title = element('AXStaticText', scenario === 'other_window' ? 'Welcome to Xcode' :
    'Select the components you want to get started with:', null);
const children = scenario === 'no_checkboxes' ? [title, button] : [title, ios, model, mac, button];
if (scenario === 'uncertain_checkbox') ios.current = null;
if (scenario === 'truncated') for (let i=0;i<500;i++) children.push(element('AXStaticText','extra',null));
if (scenario === 'deep') {
    let nested = element('AXCheckBox','Unknown download',1);
    for (let i=0;i<14;i++) nested=element('AXGroup','',null,[nested]);
    children.push(nested);
}
if (['metadata_checkbox','metadata_unknown_checkbox','title_element_checkbox'].includes(scenario)) {
    ios.name = () => null; model.name = () => null;
    ios.description = () => 'checkbox'; model.description = () => 'checkbox';
    if (scenario === 'title_element_checkbox') {
        ios.metadata.AXTitleUIElement = element('AXStaticText','iOS 26.0','iOS 26.0');
        model.metadata.AXTitleUIElement = element('AXStaticText','Predictive Code Completion Model','Predictive Code Completion Model');
    } else {
        ios.metadata.AXTitle = scenario === 'metadata_unknown_checkbox' ? 'Unknown download' : 'iOS 26.0';
        model.metadata.AXHelp = 'Predictive Code Completion Model';
    }
}
if (['metadata_button','metadata_wrong_button'].includes(scenario)) {
    button.name = () => null; button.description = () => 'button';
    button.metadata.AXTitle = scenario === 'metadata_button' ? 'Continue' : 'Delete Project';
}
const window = element('AXWindow','Xcode',null,children);
window.metadata.AXDefaultButton = button;
if (scenario === 'metadata_snapshot') {
    ios.metadata.AXIdentifier = 'platform.iphoneos';
    ios.metadata.AXHelp = 'Install iOS runtime';
    ios.metadata.AXTitleUIElement = element('AXStaticText','iOS 26.0','iOS 26.0');
    button.metadata.AXIdentifier = 'complete-onboarding';
    button.metadata.AXTitle = 'Download & Install';
}
let windows = [window];
if (scenario.startsWith('real_unnamed')) {
    const fixture = JSON.parse(fs.readFileSync(process.argv[4], 'utf8')).windows[0];
    function fromFixture(c, children=[]) {
        const node = element(c.role,c.name,c.value,children,c.enabled);
        node.description = () => c.description;
        return node;
    }
    const items = fixture.controls;
    windows = [fromFixture(items[0],items.slice(1).map(c => fromFixture(c)))];
}
const target = {name: () => 'Xcode', windows: () => windows};
const system = {
    processes: {whose: query => {
        assert.deepStrictEqual(JSON.parse(JSON.stringify(query)), {unixId:43});
        return () => [target];
    }},
    click: e => { calls.push(e.name()); if(e.role()==='AXCheckBox') e.current=0; else windows=[]; }
};
const context = {Application: name => {assert.strictEqual(name,'System Events');return system;}, JSON, Number};
vm.createContext(context);
vm.runInContext(fs.readFileSync(script,'utf8'), context);
const result = JSON.parse(context.run(['43',mode]));
process.stdout.write(JSON.stringify({result,calls}));
"""
    result = subprocess.run(['node', '-e', harness, str(script), case, mode,
                             str(script.parent / 'fixtures/xcode-26-component-chooser.json')],
                            capture_output=True, text=True, timeout=10, check=True)
    data = json.loads(result.stdout)
    assert data['result'].get('outcome') == outcome
    assert len(data['calls']) == clicks
    if case == 'metadata_snapshot':
        window = data['result']['windows'][0]
        control = next(c for c in window['controls'] if c['role'] == 'AXCheckBox' and c['enabled'])
        assert control['accessibility']['available'] == ['AXIdentifier', 'AXHelp', 'AXTitleUIElement']
        assert control['accessibility']['identifier'] == 'platform.iphoneos'
        assert control['accessibility']['titleElement']['name'] == 'iOS 26.0'
        assert control['accessibility']['position'] == [100, 200]
        assert window['defaultButton']['role'] == 'AXButton'
        assert window['defaultButton']['identifier'] == 'complete-onboarding'
        assert window['defaultButton']['title'] == 'Download & Install'
    if case.startswith('real_unnamed'):
        assert data['result']['windows'][0]['controls'][9]['name'] is None


@pytest.mark.parametrize('ci,enabled', [('true', True), ('1', True), ('false', False), ('', False)])
def test_probe_opt_in_requires_ci_and_cannot_fail_normal_unity(tmp_path, monkeypatch, ci, enabled):
    from . import diagnostics
    events = []

    class FakeProbe:
        done = False

        def __init__(self, directory, message, record=None):
            events.append('started')

        def poll(self):
            events.append('poll')
            if not self.done:
                raise RuntimeError('UI access blocked')

        def close(self):
            events.append('closed')

    monkeypatch.setattr(diagnostics, 'XcodeOnboardingProbe', FakeProbe)
    monkeypatch.setattr(diagnostics.sys, 'platform', 'darwin')
    monkeypatch.setenv('CI', ci)
    monkeypatch.setenv('UNITY_CI_XCODE_UI_PROBE', '1')
    monkeypatch.setattr(diagnostics, 'POLL_SECONDS', 0.02)
    runner = diagnostics.UnityDiagnostics(tmp_path / 'integration-test-ios.log')
    monkeypatch.setattr(runner, 'start_video', lambda label: None)
    result = runner.run([sys.executable, '-c', 'import time; time.sleep(0.1)'],
                        lambda: (onboarding.time.monotonic(), 'progress', True))
    assert result == 0
    if enabled:
        assert events[0] == 'started' and events[-1] == 'closed'
        assert 'Xcode UI probe stopped: UI access blocked' in (runner.directory / 'watchdog.log').read_text()
    else:
        assert not events
