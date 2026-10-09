"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import json
from pathlib import Path
import socket
import subprocess
import sys
import time

import pytest
import psutil

import integration_test_setup as setup


# Exercise real process/HTTP lifetimes without installing Flask or cloning schemas.
SERVER = """
import argparse, json, os
from http.server import BaseHTTPRequestHandler, HTTPServer
parser = argparse.ArgumentParser()
parser.add_argument('--addr')
parser.add_argument('--port', type=int)
args = parser.parse_args()
class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.end_headers()
    def log_message(self, *args): pass
HTTPServer((args.addr, args.port), Handler).serve_forever()
"""


@pytest.fixture
def environment(tmp_path, monkeypatch):
    server = tmp_path / 'server'
    server.mkdir()
    (server / 'app.py').write_text(SERVER)
    monkeypatch.setattr(setup, '__mock_server_root__', str(server))
    monkeypatch.setattr(setup, 'prepare_mock_server_venv', lambda: sys.executable)
    return setup.IntegrationTestEnvironment(tmp_path / 'Sample Project')


@pytest.fixture
def port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def wait_for_server(endpoint):
    deadline = time.monotonic() + 5
    while setup.server_health(endpoint) is None:
        assert time.monotonic() < deadline, 'test server did not start'
        time.sleep(0.05)


def test_unused_local_port_does_not_require_http_response(port, monkeypatch):
    monkeypatch.setattr(setup, 'build_opener', lambda *args: pytest.fail('Unused ports must not require HTTP'))
    assert setup.server_health(f'http://127.0.0.1:{port}') is None


def test_started_server_is_recorded_and_stopped(environment, port):
    process = environment.prepare('127.0.0.1', port)
    try:
        assert environment._read_state() == {
            'pid': process.pid, 'create_time': process.create_time(),
            'port': port, 'endpoint': f'http://127.0.0.1:{port}',
        }
        assert setup.server_health(environment.endpoint) is True
    finally:
        environment.finish(process)
    assert process.poll() is not None
    assert setup.server_health(environment.endpoint) is None
    assert not environment.state_path.exists()
    environment.finish()


@pytest.mark.parametrize('ci', ['true', 'false'])
def test_runs_use_lan_address(environment, port, monkeypatch, ci):
    monkeypatch.setenv('CI', ci)
    lookups = []
    def lan_address():
        lookups.append(True)
        return '127.0.0.1'
    monkeypatch.setattr(setup, 'get_reachable_inet_addr', lan_address)
    process = environment.prepare(port=port)
    try:
        assert lookups == [True]
        assert environment._read_state()['endpoint'] == f'http://127.0.0.1:{port}'
    finally:
        environment.finish(process)


@pytest.mark.parametrize('probe_error', [setup.URLError(TimeoutError('starting')), TimeoutError('starting')])
def test_startup_retries_health_timeout(environment, port, monkeypatch, probe_error):
    build_opener = setup.build_opener
    timed_out = False
    def timeout_first_request(*args):
        opener = build_opener(*args)
        request = opener.open
        def open_with_timeout(*args, **kwargs):
            nonlocal timed_out
            if not timed_out:
                timed_out = True
                raise probe_error
            return request(*args, **kwargs)
        opener.open = open_with_timeout
        return opener
    monkeypatch.setattr(setup, 'build_opener', timeout_first_request)
    process = environment.prepare('127.0.0.1', port)
    try:
        assert timed_out
        assert setup.server_health(environment.endpoint) is True
    finally:
        environment.finish(process)


def test_live_pid_returns_without_http_or_bootstrap(environment, port, monkeypatch):
    process = environment.prepare('127.0.0.1', port)
    state = environment._read_state()
    try:
        helper = setup.IntegrationTestEnvironment(environment.directory.parent.parent)
        monkeypatch.setattr(setup, 'server_health', lambda *args, **kwargs: pytest.fail('A live PID must return directly'))
        monkeypatch.setattr(setup, 'prepare_mock_server_venv', lambda: pytest.fail('A live PID must not bootstrap'))
        monkeypatch.setattr(setup, 'get_reachable_inet_addr', lambda: pytest.fail('A live PID must reuse the saved endpoint'))
        assert helper.prepare() is None
        assert helper.endpoint == state['endpoint']
        assert helper._read_state() == state
    finally:
        environment.finish(process)


def test_cleanup_stops_a_reused_recorded_server(environment, port):
    process = environment.prepare('127.0.0.1', port)
    try:
        helper = setup.IntegrationTestEnvironment(environment.directory.parent.parent)
        assert helper.prepare() is None
        helper.finish()
        process.wait(timeout=5)
        assert not environment.state_path.exists()
    finally:
        environment.finish(process)


def test_untracked_server_is_not_adopted_from_health(environment, port):
    process = subprocess.Popen([sys.executable, 'app.py', '--addr', '127.0.0.1', '--port', str(port)],
                               cwd=setup.__mock_server_root__)
    try:
        wait_for_server(f'http://127.0.0.1:{port}')
        with pytest.raises(RuntimeError, match='without a live state record'):
            environment.prepare('127.0.0.1', port)
        assert not environment.state_path.exists()
        assert process.poll() is None
    finally:
        process.terminate()
        process.wait(timeout=5)


def test_cleanup_can_read_state_in_a_fresh_helper(environment, port):
    process = environment.prepare('127.0.0.1', port)
    try:
        setup.IntegrationTestEnvironment(environment.directory.parent.parent).finish()
        process.wait(timeout=5)
        assert not environment.state_path.exists()
    finally:
        environment.finish(process)


def test_dependency_failure_cleans_state(environment, port, monkeypatch):
    def fail():
        raise RuntimeError('dependency setup failed')
    monkeypatch.setattr(setup, 'prepare_mock_server_venv', fail)
    with pytest.raises(RuntimeError, match='dependency setup failed'):
        environment.prepare('127.0.0.1', port)
    assert not environment.state_path.exists()


def test_non_success_healthcheck_is_rejected(environment, port):
    (Path(setup.__mock_server_root__) / 'app.py').write_text(SERVER.replace('self.send_response(200)', 'self.send_response(503)'))
    process = subprocess.Popen([sys.executable, 'app.py', '--addr', '127.0.0.1', '--port', str(port)],
                               cwd=setup.__mock_server_root__)
    try:
        with pytest.raises(RuntimeError, match='Cannot probe mock server'):
            wait_for_server(f'http://127.0.0.1:{port}')
        with pytest.raises(RuntimeError, match='Cannot probe mock server'):
            environment.prepare('127.0.0.1', port)
        assert process.poll() is None
    finally:
        process.terminate()
        process.wait(timeout=5)


def test_startup_failure_cleans_state(environment, port):
    (Path(setup.__mock_server_root__) / 'app.py').write_text('raise SystemExit(1)')
    with pytest.raises(RuntimeError, match='Mock server exited'):
        environment.prepare('127.0.0.1', port)
    assert not environment.state_path.exists()


def test_server_cleanup_leaves_settings_backup_untouched(environment, port):
    project = environment.directory.parent.parent
    settings = project / 'Assets/Resources/DatadogSettings.asset'
    backup = environment.directory / 'DatadogSettings.original'
    settings.parent.mkdir(parents=True)
    backup.parent.mkdir(parents=True)
    settings.write_bytes(b'original settings')
    backup.write_bytes(b'original snapshot')
    process = environment.prepare('127.0.0.1', port)
    environment.finish(process)
    assert settings.read_bytes() == b'original settings'
    assert backup.read_bytes() == b'original snapshot'


def test_dead_pid_starts_a_new_server(environment, port):
    process = environment.prepare('127.0.0.1', port)
    process.terminate()
    process.wait(timeout=5)
    replacement = environment.prepare('127.0.0.1', port)
    try:
        assert replacement is not None
        assert environment._read_state()['pid'] == replacement.pid
    finally:
        environment.finish(replacement)


def test_cleanup_removes_dead_pid_state(environment, port):
    process = environment.prepare('127.0.0.1', port)
    process.terminate()
    process.wait(timeout=5)
    environment.finish()
    assert not environment.state_path.exists()


def test_cleanup_stops_reloader_parent_and_serving_child(environment, port):
    # Model the reloader: a parent waits while its child serves requests.
    parent = "import subprocess, sys\nchild = subprocess.Popen([sys.executable, '-c', " + repr(SERVER) + ", *sys.argv[1:]])\nchild.wait()\n"
    (Path(setup.__mock_server_root__) / 'app.py').write_text(parent)
    process = environment.prepare('127.0.0.1', port)
    try:
        assert setup.server_health(environment.endpoint) is True
        assert environment._read_state()['pid'] == process.pid
    finally:
        environment.finish(process)
    deadline = time.monotonic() + 5
    while setup.server_health(environment.endpoint) is not None:
        assert time.monotonic() < deadline, 'serving child survived cleanup'
        time.sleep(0.05)
    assert not environment.state_path.exists()


@pytest.mark.parametrize('action', ['prepare', 'finish'])
def test_stale_identity_does_not_signal_an_unrelated_process(environment, port, action):
    unrelated = psutil.Popen([sys.executable, '-c', 'import time; time.sleep(60)'], start_new_session=True)
    environment.directory.mkdir(parents=True)
    environment._write_state({'pid': unrelated.pid, 'create_time': unrelated.create_time() + 1,
                              'port': port, 'endpoint': f'http://127.0.0.1:{port}'})
    replacement = None
    try:
        if action == 'prepare':
            replacement = environment.prepare('127.0.0.1', port)
            assert replacement is not None
        else:
            environment.finish()
            assert not environment.state_path.exists()
        assert unrelated.poll() is None
    finally:
        if replacement is not None:
            environment.finish(replacement)
        unrelated.terminate()
        unrelated.wait(timeout=5)


def wait_for_zombie(process):
    deadline = time.monotonic() + 5
    while process.status() != psutil.STATUS_ZOMBIE:
        assert time.monotonic() < deadline, 'child did not exit'
        time.sleep(0.01)


def test_group_discovery_rejects_pid_reuse(monkeypatch):
    generation = 0

    class Process:
        pid = 12345

        def __init__(self, pid):
            self.generation = generation

        def is_running(self):
            return self.generation == generation

        def status(self):
            return psutil.STATUS_RUNNING

    candidate = Process(12345)

    def reused_group(pid):
        nonlocal generation
        generation += 1
        return 12345

    monkeypatch.setattr(psutil, 'process_iter', lambda: [candidate])
    monkeypatch.setattr(psutil, 'Process', Process)
    monkeypatch.setattr(setup.os, 'getpgid', reused_group)
    assert setup._live_group_members(12345) == {}


def test_unreaped_zombie_starts_a_replacement(environment, port):
    process = environment.prepare('127.0.0.1', port)
    replacement = None
    try:
        process.terminate()
        wait_for_zombie(process)
        assert not setup.process_is_running(environment._read_state())
        replacement = environment.prepare('127.0.0.1', port)
        assert replacement is not None
        assert replacement.pid != process.pid
    finally:
        process.wait(timeout=5)
        if replacement is not None:
            environment.finish(replacement)


def test_fresh_cleanup_waits_for_delayed_shutdown(environment, port):
    delayed = SERVER.replace('HTTPServer((args.addr, args.port), Handler).serve_forever()', '''
import signal, time
stop_at = None
def stop(*args):
    global stop_at
    stop_at = time.monotonic() + 0.3
signal.signal(signal.SIGTERM, stop)
server = HTTPServer((args.addr, args.port), Handler)
server.timeout = 0.05
while stop_at is None or time.monotonic() < stop_at:
    server.handle_request()
server.server_close()
''')
    (Path(setup.__mock_server_root__) / 'app.py').write_text(delayed)
    process = environment.prepare('127.0.0.1', port)
    replacement = None
    try:
        helper = setup.IntegrationTestEnvironment(environment.directory.parent.parent)
        helper.finish()
        assert setup.server_health(environment.endpoint) is None
        replacement = helper.prepare('127.0.0.1', port)
        assert replacement is not None
    finally:
        process.wait(timeout=5)
        if replacement is not None:
            helper.finish(replacement)


@pytest.mark.parametrize('reap_leader', [False, True])
def test_orphaned_reloader_group(environment, port, reap_leader):
    root = Path(setup.__mock_server_root__)
    parent = '''import os, subprocess, sys, time
from pathlib import Path
child = subprocess.Popen([sys.executable, '-c', SERVER, *sys.argv[1:]])
Path('worker.pid').write_text(str(child.pid))
while not Path('exit-parent').exists(): time.sleep(0.01)
os._exit(0)
'''.replace('SERVER', repr(SERVER))
    (root / 'app.py').write_text(parent)
    process = environment.prepare('127.0.0.1', port)
    worker = psutil.Process(int((root / 'worker.pid').read_text()))
    try:
        (root / 'exit-parent').touch()
        wait_for_zombie(process)
        if reap_leader:
            process.wait(timeout=5)
            with pytest.raises(RuntimeError, match='leader.*gone'):
                environment.finish()
            assert environment.state_path.exists()
            assert setup.server_health(environment.endpoint) is True
        else:
            environment.finish()
            assert not environment.state_path.exists()
            assert setup.server_health(environment.endpoint) is None
    finally:
        if setup._is_live(worker):
            worker.terminate()
        worker.wait(timeout=5)
        process.wait(timeout=5)
        environment.finish()


def test_cleanup_escalates_when_term_is_ignored(environment, port, monkeypatch):
    server = SERVER.replace('import argparse, json, os',
                            'import argparse, json, os, signal\nsignal.signal(signal.SIGTERM, signal.SIG_IGN)')
    (Path(setup.__mock_server_root__) / 'app.py').write_text(server)
    monkeypatch.setattr(setup, 'STOP_TIMEOUT', 0.2)
    process = environment.prepare('127.0.0.1', port)
    try:
        setup.IntegrationTestEnvironment(environment.directory.parent.parent).finish()
        assert setup.server_health(environment.endpoint) is None
        assert not environment.state_path.exists()
    finally:
        process.wait(timeout=5)


def test_permission_failure_retains_state(environment, port, monkeypatch):
    process = environment.prepare('127.0.0.1', port)
    state = environment._read_state()
    try:
        with monkeypatch.context() as patch:
            def denied(*args):
                raise psutil.AccessDenied(process.pid)
            patch.setattr(setup, '_live_group_members', denied)
            with pytest.raises(psutil.AccessDenied):
                environment.finish()
            assert environment._read_state() == state
            assert process.poll() is None
    finally:
        environment.finish(process)


def test_local_cleanup_preserves_a_replacement_record(environment, port):
    process = environment.prepare('127.0.0.1', port)
    environment.state_path.unlink()
    helper = setup.IntegrationTestEnvironment(environment.directory.parent.parent)
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        replacement_port = sock.getsockname()[1]
    replacement = helper.prepare('127.0.0.1', replacement_port)
    state = helper._read_state()
    try:
        environment.finish(process)
        assert process.poll() is not None
        assert replacement.poll() is None
        assert helper._read_state() == state
        assert setup.server_health(helper.endpoint) is True
    finally:
        helper.finish(replacement)


def test_legacy_record_is_discarded_without_signaling(environment, port):
    process = environment.prepare('127.0.0.1', port)
    state = environment._read_state()
    del state['create_time']
    environment._write_state(state)
    try:
        environment.finish()
        assert process.poll() is None
        assert not environment.state_path.exists()
    finally:
        environment.finish(process)
