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

import integration_test_setup as setup


# Exercise actual process/HTTP lifetimes without installing Flask or cloning schemas
# in the Python unit suite. The real Flask endpoint is covered by the smoke run.
SERVER = '''
import argparse, json, os
from http.server import BaseHTTPRequestHandler, HTTPServer
parser = argparse.ArgumentParser()
parser.add_argument('--addr')
parser.add_argument('--port', type=int)
parser.add_argument('--test-owner', default='')
args = parser.parse_args()
class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.end_headers()
        self.wfile.write(json.dumps(dict(service='datadog-unity-mock-server',
            version=1, pid=os.getpid(), owner=args.test_owner)).encode())
    def log_message(self, *args): pass
HTTPServer((args.addr, args.port), Handler).serve_forever()
'''


@pytest.fixture
def environment(tmp_path, monkeypatch):
    project = tmp_path / 'Sample Project'
    server = tmp_path / 'server'
    server.mkdir()
    (server / 'app.py').write_text(SERVER)
    monkeypatch.setattr(setup, '__mock_server_root__', str(server))
    monkeypatch.setattr(setup, 'prepare_mock_server_venv', lambda: sys.executable)
    return setup.IntegrationTestEnvironment(project)


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
    def unexpected_http(*args):
        pytest.fail('An unused local port must not require an HTTP connection')

    monkeypatch.setattr(setup, 'build_opener', unexpected_http)
    assert setup.server_health(f'http://127.0.0.1:{port}') is None


def test_owned_server_stays_ready_until_completion(environment, port):
    endpoint = f'http://127.0.0.1:{port}'
    try:
        environment.prepare('127.0.0.1', port)
        state = json.loads(environment.state_path.read_text())
        assert state['endpoint'] == endpoint
        assert setup.server_health(endpoint)['owner'] == state['owner']
    finally:
        # Recreate the helper as Unity does across callbacks and domain reloads.
        setup.IntegrationTestEnvironment(environment.project).finish()
    assert setup.server_health(endpoint) is None
    assert not environment.state_path.exists()
    environment.finish()


@pytest.mark.parametrize('platform,client_host', [('ios', 'localhost'), ('android', '10.0.2.2')])
def test_ci_mobile_clients_use_loopback_routing(environment, port, monkeypatch, platform, client_host):
    monkeypatch.setenv('CI', 'true')
    server_health = setup.server_health
    probed_endpoints = []

    def probe(endpoint, *args, **kwargs):
        probed_endpoints.append(endpoint)
        return server_health(endpoint, *args, **kwargs)

    def unexpected_lan_lookup():
        pytest.fail('CI mobile tests must not select a LAN address')

    monkeypatch.setattr(setup, 'server_health', probe)
    monkeypatch.setattr(setup, 'get_reachable_inet_addr', unexpected_lan_lookup)
    try:
        environment.prepare(port=port, platform=platform)
        state = json.loads(environment.state_path.read_text())
        assert state['endpoint'] == f'http://{client_host}:{port}'
        assert state['server_endpoint'] == f'http://127.0.0.1:{port}'
    finally:
        environment.finish()
    assert probed_endpoints and set(probed_endpoints) == {f'http://127.0.0.1:{port}'}
    assert server_health(f'http://127.0.0.1:{port}') is None
    assert not environment.state_path.exists()


@pytest.mark.parametrize('ci,platform', [('true', 'standaloneosx'), ('false', 'ios'), ('false', 'android')])
def test_other_runs_keep_current_address_selection(environment, port, monkeypatch, ci, platform):
    monkeypatch.setenv('CI', ci)
    lan_lookups = []

    def lan_address():
        lan_lookups.append(True)
        return '127.0.0.1'

    monkeypatch.setattr(setup, 'get_reachable_inet_addr', lan_address)
    try:
        environment.prepare(port=port, platform=platform)
        state = json.loads(environment.state_path.read_text())
        assert lan_lookups == [True]
        assert state['endpoint'] == f'http://127.0.0.1:{port}'
    finally:
        environment.finish()
    assert not environment.state_path.exists()


@pytest.mark.parametrize('probe_error', [
    setup.URLError(TimeoutError('server is still starting')),
    TimeoutError('server is still starting'),
])
def test_owned_startup_retries_a_health_probe_timeout(environment, port, monkeypatch, probe_error):
    build_opener = setup.build_opener
    timed_out = False

    def timeout_first_request(*args):
        opener = build_opener(*args)
        open_request = opener.open

        def open_with_timeout(*args, **kwargs):
            nonlocal timed_out
            if not timed_out:
                timed_out = True
                raise probe_error
            return open_request(*args, **kwargs)

        opener.open = open_with_timeout
        return opener

    monkeypatch.setattr(setup, 'build_opener', timeout_first_request)
    try:
        environment.prepare('127.0.0.1', port)
        assert timed_out, 'The regression must exercise a timed-out HTTP probe'
        state = json.loads(environment.state_path.read_text())
        health = setup.server_health(f'http://127.0.0.1:{port}')
        assert health['owner'] == state['owner']
        assert health['pid'] == state['pid']
    finally:
        environment.finish()
    assert not environment.state_path.exists()


def test_existing_server_health_timeout_does_not_start_another_server(environment, port, monkeypatch):
    process = subprocess.Popen([sys.executable, 'app.py', '--addr', '127.0.0.1', '--port', str(port)],
                               cwd=setup.__mock_server_root__)
    try:
        wait_for_server(f'http://127.0.0.1:{port}')

        class TimedOutOpener:
            def open(self, *args, **kwargs):
                raise setup.URLError(TimeoutError('existing server is unresponsive'))

        def unexpected_bootstrap():
            pytest.fail('A timeout from an existing server must not start a replacement')

        monkeypatch.setattr(setup, 'build_opener', lambda *args: TimedOutOpener())
        monkeypatch.setattr(setup, 'prepare_mock_server_venv', unexpected_bootstrap)
        with pytest.raises(RuntimeError, match='Cannot probe mock server'):
            environment.prepare('127.0.0.1', port)
        assert process.poll() is None
        assert not environment.state_path.exists()
    finally:
        process.terminate()
        process.wait(timeout=5)


@pytest.mark.parametrize('owner', ['', 'another-test-run'])
def test_external_server_is_never_stopped(environment, port, owner):
    endpoint = f'http://127.0.0.1:{port}'
    process = subprocess.Popen([sys.executable, 'app.py', '--addr', '127.0.0.1', '--port', str(port),
                                '--test-owner', owner], cwd=setup.__mock_server_root__)
    try:
        wait_for_server(endpoint)
        if owner:
            with pytest.raises(RuntimeError, match='another test run'):
                environment.prepare('127.0.0.1', port)
        else:
            environment.prepare('127.0.0.1', port)
        environment.finish()
        assert process.poll() is None
        assert setup.server_health(endpoint)['pid'] == process.pid
    finally:
        environment.finish()
        process.terminate()
        process.wait(timeout=5)


def test_dependency_failure_cleans_up_state(environment, port, monkeypatch):
    def fail_bootstrap():
        raise RuntimeError('dependency setup failed')

    monkeypatch.setattr(setup, 'prepare_mock_server_venv', fail_bootstrap)
    with pytest.raises(RuntimeError, match='dependency setup failed'):
        environment.prepare('127.0.0.1', port)
    assert setup.server_health(f'http://127.0.0.1:{port}') is None
    assert not environment.state_path.exists()


def test_unrecognized_server_is_rejected(environment, port):
    (Path(setup.__mock_server_root__) / 'app.py').write_text(SERVER.replace('version=1', 'version=2'))
    process = subprocess.Popen([sys.executable, 'app.py', '--addr', '127.0.0.1', '--port', str(port)],
                               cwd=setup.__mock_server_root__)
    try:
        with pytest.raises(RuntimeError, match='unrecognized server'):
            wait_for_server(f'http://127.0.0.1:{port}')
        with pytest.raises(RuntimeError, match='unrecognized server'):
            environment.prepare('127.0.0.1', port)
        assert process.poll() is None
        assert not environment.state_path.exists()
    finally:
        process.terminate()
        process.wait(timeout=5)


def test_server_startup_failure_cleans_up_state(environment, port):
    (Path(setup.__mock_server_root__) / 'app.py').write_text('raise SystemExit(1)')
    with pytest.raises(RuntimeError, match='Mock server exited'):
        environment.prepare('127.0.0.1', port)
    assert not environment.state_path.exists()


def test_next_setup_reuses_owned_server_across_editor_reloads(environment, port, monkeypatch):
    try:
        environment.prepare('127.0.0.1', port)
        first_health = setup.server_health(f'http://127.0.0.1:{port}')
        monkeypatch.setattr(setup, 'prepare_mock_server_venv', lambda: pytest.fail('Reusing a server must not install or restart it'))
        assert environment.prepare('127.0.0.1', port) is False
        assert setup.server_health(f'http://127.0.0.1:{port}') == first_health
    finally:
        environment.finish()


def test_server_lifecycle_leaves_settings_and_editor_backup_untouched(environment, port):
    settings = environment.project / 'Assets/Resources/DatadogSettings.asset'
    backup = environment.directory / 'DatadogSettings.original'
    settings.parent.mkdir(parents=True)
    backup.parent.mkdir(parents=True)
    settings.write_bytes(b'Settings belong to the Editor, not the server helper.')
    backup.write_bytes(b'Original asset snapshot created by C#.')
    try:
        environment.prepare('127.0.0.1', port)
    finally:
        environment.finish()
    assert settings.read_bytes() == b'Settings belong to the Editor, not the server helper.'
    assert backup.read_bytes() == b'Original asset snapshot created by C#.'


def test_cleanup_does_not_signal_pid_when_server_owner_changed(environment, port):
    environment.prepare('127.0.0.1', port)
    state = json.loads(environment.state_path.read_text())
    try:
        environment.state_path.write_text(json.dumps({**state, 'owner': 'stale-owner'}))
        environment.finish()
        assert setup.server_health(f'http://127.0.0.1:{port}')['owner'] == state['owner']
    finally:
        environment.state_path.write_text(json.dumps(state))
        environment.finish()


def test_stale_state_starts_a_new_server(environment, port):
    assert environment.prepare('127.0.0.1', port) is True
    stale = environment._read_state()
    environment.finish()
    environment._write_state(stale)
    try:
        assert environment.prepare('127.0.0.1', port) is True
        assert environment._read_state()['owner'] != stale['owner']
    finally:
        environment.finish()


def test_cleanup_with_borrowed_owner_cannot_stop_replacement(environment, port):
    environment.prepare('127.0.0.1', port)
    try:
        environment.finish(expected_owner='a-previous-run')
        assert environment.state_path.exists()
        assert setup.server_health(f'http://127.0.0.1:{port}') is not None
    finally:
        environment.finish()
