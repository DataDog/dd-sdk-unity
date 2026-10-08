"""Mock-server startup and cleanup for integration tests.

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

from common.inet_addr import get_reachable_inet_addr
from common.log import init_logger
from common.mockserver import prepare_mock_server_venv, __mock_server_root__


STATE_DIRECTORY = Path('Library/DatadogIntegrationTests')
HEALTH_PATH = '/_healthcheck'
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
            if response.status != 200:
                raise RuntimeError(f'Healthcheck at {endpoint} returned HTTP {response.status}')
            return True
    except URLError as error:
        if isinstance(error.reason, ConnectionError) or (retry_timeout and isinstance(error.reason, TimeoutError)):
            return None
        raise RuntimeError(f'Cannot probe mock server at {endpoint}: {error}') from error
    except (ConnectionError, RemoteDisconnected):
        # A server can close an in-flight health request during shutdown.
        return None
    except TimeoutError as error:
        if retry_timeout:
            return None
        raise RuntimeError(f'Cannot probe mock server at {endpoint}: {error}') from error


def process_is_running(pid):
    if pid <= 0:
        return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


class IntegrationTestEnvironment:
    def __init__(self, project):
        self.directory = Path(project).resolve() / STATE_DIRECTORY
        self.state_path = self.directory / 'state.json'
        self.endpoint = None

    def _read_state(self):
        return json.loads(self.state_path.read_text()) if self.state_path.exists() else None

    def _write_state(self, state):
        temporary = self.state_path.with_suffix('.tmp')
        temporary.write_text(json.dumps(state))
        temporary.replace(self.state_path)

    def prepare(self, host=None, port=PORT):
        """Reuse a live recorded PID; otherwise start a server and return its Popen reference."""
        state = self._read_state()
        if state and process_is_running(state['pid']):
            self.endpoint = state['endpoint']
            print(f'Integration mock server reused at {self.endpoint}', flush=True)
            return None
        if state:
            self.finish()

        host = host or get_reachable_inet_addr()
        if not host:
            raise RuntimeError('Failed to resolve a reachable LAN address for the mock server')
        self.endpoint = f'http://{host}:{port}'
        if server_health(self.endpoint):
            raise RuntimeError(f'Mock server is already running at {self.endpoint} without a live state record')
        self.directory.mkdir(parents=True, exist_ok=True)

        process = None
        try:
            python = prepare_mock_server_venv()
            with (self.directory / 'mock-server.log').open('ab') as log:
                process = subprocess.Popen(
                    [python, 'app.py', '--addr', host, '--port', str(port)],
                    cwd=__mock_server_root__, stdout=log, stderr=subprocess.STDOUT,
                    stdin=subprocess.DEVNULL, start_new_session=True,
                )
            self._write_state({'pid': process.pid, 'port': port, 'endpoint': self.endpoint})
            deadline = time.monotonic() + 15
            while True:
                if process.poll() is not None:
                    raise RuntimeError(f'Mock server exited; see {self.directory / "mock-server.log"}')
                health = server_health(self.endpoint, retry_timeout=True)
                if health:
                    break
                if time.monotonic() >= deadline:
                    raise RuntimeError(f'Mock server did not become ready at {self.endpoint}')
                time.sleep(0.1)
        except BaseException:
            if process is not None:
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait(timeout=5)
            self.state_path.unlink(missing_ok=True)
            raise
        print(f'Integration mock server ready at {self.endpoint}', flush=True)
        return process

    def finish(self, process=None):
        """Stop the recorded process group, including reloader workers."""
        state = self._read_state()
        if state is None:
            return
        pid = state['pid']
        if pid <= 0:
            raise RuntimeError('Invalid mock-server PID')
        try:
            # Each managed server starts a new group; its reloader workers stay in that group.
            os.killpg(pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        if process is not None and process.pid == pid:
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(pid, signal.SIGKILL)
                process.wait(timeout=5)
        self.state_path.unlink(missing_ok=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['prepare', 'finish'])
    parser.add_argument('--project', required=True)
    args = parser.parse_args()
    init_logger()
    environment = IntegrationTestEnvironment(args.project)
    if args.action == 'prepare':
        environment.prepare()
    else:
        environment.finish()
