"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
from pathlib import Path
import subprocess
import sys
import time

import pytest

from . import diagnostics


@pytest.fixture
def watchdog(tmp_path, monkeypatch):
    monkeypatch.setattr(diagnostics, 'HEARTBEAT_SECONDS', 0.05)
    monkeypatch.setattr(diagnostics, 'STALL_SECONDS', 0.1)
    monkeypatch.setattr(diagnostics, 'UNITY_TIMEOUT_SECONDS', 0.5)
    monkeypatch.setattr(diagnostics, 'POLL_SECONDS', 0.02)
    runner = diagnostics.UnityDiagnostics(str(tmp_path / 'integration-test-ios.log'))
    # These tests never record the developer's desktop.
    monkeypatch.setattr(runner, 'start_video', lambda label: None)
    return runner


def blocked_child():
    return [sys.executable, '-c', 'import threading; threading.Event().wait()']


def idle_status():
    return time.monotonic() - 60, 'Last build step', True


def test_timeout_captures_stacks_and_reaps_owned_process(watchdog, monkeypatch):
    commands = []

    def tool(command, output, timeout=15):
        commands.append(command)
        if command[0] == '/bin/ps':
            output.write_text(f'{watchdog.process.pid} {os.getpid()} 0.1 1234 00:01 S /fake/Unity\n')
        else:
            output.write_text('Native sampling command\n')
            Path(command[-1]).write_text('Native thread snapshot\n')
    monkeypatch.setattr(watchdog, 'tool', tool)

    with pytest.raises(RuntimeError, match='Unity exceeded'):
        watchdog.run(blocked_child(), idle_status)

    assert watchdog.process.poll() is not None
    with pytest.raises(ProcessLookupError):
        os.kill(watchdog.process.pid, 0)
    assert list(watchdog.directory.glob('*-python-threads.log'))
    assert list(watchdog.directory.glob('*-processes.log'))
    if sys.platform == 'darwin':
        assert list(watchdog.directory.glob('*-process-*-sample.log'))
    assert 'args=' not in commands[0][-1]
    log = (watchdog.directory / 'watchdog.log').read_text()
    assert 'log reader alive=True' in log
    assert 'Last build step' in log
    assert 'Unity runtime limit exceeded' in log


def test_cancellation_reaps_owned_process(watchdog):
    def cancelled():
        raise KeyboardInterrupt
    with pytest.raises(KeyboardInterrupt):
        watchdog.run(blocked_child(), cancelled)
    assert watchdog.process.poll() is not None


def test_normal_exit_preserves_status(watchdog):
    result = watchdog.run([sys.executable, '-c', 'raise SystemExit(2)'], idle_status)
    assert result == 2
    assert 'exited with 2' in (watchdog.directory / 'watchdog.log').read_text()


def recorder_runner(tmp_path, monkeypatch, recorder_script):
    monkeypatch.setattr(diagnostics, 'POLL_SECONDS', 0.02)
    monkeypatch.setattr(diagnostics, 'HEARTBEAT_SECONDS', 0.05)
    monkeypatch.setattr(diagnostics, 'UNITY_TIMEOUT_SECONDS', 5)
    runner = diagnostics.UnityDiagnostics(str(tmp_path / 'integration-test-ios.log'))
    runner.video_disabled = False
    real_popen = subprocess.Popen
    recorders = []

    def popen(command, *args, **kwargs):
        if command[0] == '/usr/sbin/screencapture':
            assert '-g' not in command
            child = real_popen([sys.executable, '-c', recorder_script], *args, **kwargs)
            recorders.append(child)
            return child
        return real_popen(command, *args, **kwargs)
    monkeypatch.setattr(diagnostics.subprocess, 'Popen', popen)
    return runner, recorders


def test_recording_permission_failure_does_not_fail_unity(tmp_path, monkeypatch):
    runner, recorders = recorder_runner(
        tmp_path, monkeypatch,
        'import sys; sys.stderr.write("Screen recording permission denied"); raise SystemExit(1)',
    )
    result = runner.run(
        [sys.executable, '-c', 'import time; time.sleep(0.3)'],
        lambda: (time.monotonic(), 'progress', True),
    )
    assert result == 0
    assert len(recorders) == 1
    assert recorders[0].poll() is not None
    assert runner.video_disabled
    assert 'permission denied' in (runner.directory / 'screen-capture.log').read_text()


def test_blocked_recorder_is_bounded_and_reaped(tmp_path, monkeypatch):
    monkeypatch.setattr(diagnostics, 'VIDEO_SECONDS', 0.02)
    monkeypatch.setattr(diagnostics, 'VIDEO_GRACE_SECONDS', 0.02)
    runner, recorders = recorder_runner(tmp_path, monkeypatch, 'import threading; threading.Event().wait()')
    result = runner.run(
        [sys.executable, '-c', 'import time; time.sleep(0.3)'],
        lambda: (time.monotonic(), 'progress', True),
    )
    assert result == 0
    assert len(recorders) == 1
    assert recorders[0].poll() is not None
    assert runner.video_disabled
    assert 'Screen recorder exceeded its limit' in (runner.directory / 'watchdog.log').read_text()
