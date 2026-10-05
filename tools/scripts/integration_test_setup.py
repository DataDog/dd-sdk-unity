"""Mock-server lifecycle for Unity's integration-test hooks and command-line cleanup.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import argparse
import errno
from http.client import RemoteDisconnected
import json
import os
from pathlib import Path
import signal
import socket
import subprocess
import time
from urllib.error import URLError
from urllib.request import ProxyHandler, build_opener
from urllib.parse import urlsplit
import uuid

from common.inet_addr import get_reachable_inet_addr
from common.log import init_logger
from common.mockserver import prepare_mock_server_venv, __mock_server_root__


STATE_DIRECTORY = Path('Library/DatadogIntegrationTests')
HEALTH_PATH = '/__datadog_test_health'
PORT = 5100


def server_health(endpoint: str, retry_timeout=False):
    # macOS can silently drop connections to an unused LAN port. Check whether
    # this local address is free before issuing HTTP, rather than mistaking that
    # firewall timeout for an existing server. Still verify any occupied port.
    address = urlsplit(endpoint)
    with socket.socket() as probe:
        probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            probe.bind((address.hostname, address.port))
        except OSError as error:
            if error.errno != errno.EADDRINUSE:
                raise
        else:
            return None
    # Local test traffic must not go through a machine's HTTP proxy.
    try:
        with build_opener(ProxyHandler({})).open(endpoint + HEALTH_PATH, timeout=1) as response:
            health = json.load(response)
    except URLError as error:
        if isinstance(error.reason, ConnectionError) or (retry_timeout and isinstance(error.reason, TimeoutError)):
            return None
        raise RuntimeError(f'Cannot probe mock server at {endpoint}: {error}') from error
    except (ConnectionError, RemoteDisconnected):
        # A server can close an in-flight health request during shutdown.
        return None
    except (ValueError, TimeoutError) as error:
        if retry_timeout and isinstance(error, TimeoutError):
            return None
        raise RuntimeError(f'Port at {endpoint} is occupied by an unrecognized server') from error
    if not isinstance(health, dict) or health.get('service') != 'datadog-unity-mock-server' or health.get('version') != 1:
        raise RuntimeError(f'Port at {endpoint} is occupied by an unrecognized server')
    return health


class IntegrationTestEnvironment:
    def __init__(self, project):
        self.project = Path(project).resolve()
        self.directory = self.project / STATE_DIRECTORY
        self.state_path = self.directory / 'state.json'

    def _read_state(self):
        return json.loads(self.state_path.read_text()) if self.state_path.exists() else None

    def _write_state(self, state):
        temporary = self.state_path.with_suffix('.tmp')
        temporary.write_text(json.dumps(state))
        temporary.replace(self.state_path)

    def prepare(self, host=None, port=PORT, platform=None):
        # Stop an owned server left behind by a previous failed/cancelled run.
        self.finish()
        ci = os.environ.get('CI', '').lower() in ('true', '1')
        if ci and platform in ('ios', 'android'):
            host = '127.0.0.1'
            client_host = 'localhost' if platform == 'ios' else '10.0.2.2'
        else:
            host = host or get_reachable_inet_addr()
            client_host = host
        if not host:
            raise RuntimeError('Failed to resolve a reachable LAN address for the mock server')
        endpoint = f'http://{client_host}:{port}'
        server_endpoint = f'http://{host}:{port}'
        health = server_health(server_endpoint)
        if health and health.get('owner'):
            raise RuntimeError(f'Mock server at {server_endpoint} belongs to another test run')

        self.directory.mkdir(parents=True, exist_ok=True)
        state = {'endpoint': endpoint, 'server_endpoint': server_endpoint, 'owner': '', 'pid': None}
        self._write_state(state)
        process = None
        try:
            if health is None:
                python = prepare_mock_server_venv()
                state['owner'] = uuid.uuid4().hex
                with (self.directory / 'mock-server.log').open('ab') as log:
                    process = subprocess.Popen(
                        [python, 'app.py', '--addr', host, '--port', str(port), '--test-owner', state['owner']],
                        cwd=__mock_server_root__, stdout=log, stderr=subprocess.STDOUT,
                        stdin=subprocess.DEVNULL, start_new_session=True,
                    )
                state['pid'] = process.pid
                self._write_state(state)
                deadline = time.monotonic() + 15
                while True:
                    if process.poll() is not None:
                        raise RuntimeError(f'Mock server exited; see {self.directory / "mock-server.log"}')
                    # Binding a socket can precede listening and HTTP readiness.
                    health = server_health(server_endpoint, retry_timeout=True)
                    if health:
                        if health.get('owner') != state['owner'] or health.get('pid') != process.pid:
                            raise RuntimeError('Another server occupied the mock-server port during startup')
                        break
                    if time.monotonic() >= deadline:
                        raise RuntimeError(f'Mock server did not become ready at {server_endpoint}')
                    time.sleep(0.1)

        except BaseException:
            if process is not None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
            # The child is known to have stopped, so no PID lookup is needed here.
            state['owner'] = ''
            self._write_state(state)
            self.finish()
            raise
        print(f'Integration mock server ready at {endpoint} ({"owned" if state["owner"] else "existing"} server)', flush=True)

    def finish(self):
        state = self._read_state()
        if not state:
            return
        if state['owner'] and state['pid']:
            server_endpoint = state.get('server_endpoint', state['endpoint'])
            health = server_health(server_endpoint)
            # Never signal an unrelated process, even if the OS has reused its PID.
            if health and health.get('owner') == state['owner'] and health.get('pid') == state['pid']:
                os.kill(state['pid'], signal.SIGTERM)
                deadline = time.monotonic() + 5
                while server_health(server_endpoint) == health:
                    if time.monotonic() >= deadline:
                        raise RuntimeError('Owned mock server did not stop; retry cleanup')
                    time.sleep(0.1)
        self.state_path.unlink()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['prepare', 'finish'])
    parser.add_argument('--project', required=True)
    parser.add_argument('--platform', type=str.lower, help='Unity build platform for CI simulator routing')
    args = parser.parse_args()
    init_logger()
    environment = IntegrationTestEnvironment(args.project)
    if args.action == 'prepare':
        environment.prepare(platform=args.platform)
    else:
        environment.finish()
