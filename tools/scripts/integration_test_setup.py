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

import psutil

from common.inet_addr import get_reachable_inet_addr
from common.log import init_logger
from common.mockserver import prepare_mock_server_venv, __mock_server_root__


STATE_DIRECTORY = Path('Library/DatadogIntegrationTests')
HEALTH_PATH = '/_healthcheck'
PORT = 5100
STOP_TIMEOUT = 5


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


def _verified_process(state):
    if state['pid'] <= 0 or 'create_time' not in state:
        return None
    try:
        process = psutil.Process(state['pid'])
        if process.create_time() == state['create_time']:
            # macOS cannot query a zombie's group; its identity still anchors the saved group.
            if process.status() == psutil.STATUS_ZOMBIE or os.getpgid(process.pid) == process.pid:
                return process
    except (psutil.NoSuchProcess, ProcessLookupError):
        pass
    return None


def _is_live(process):
    try:
        return process.is_running() and process.status() not in (psutil.STATUS_ZOMBIE, psutil.STATUS_DEAD)
    except psutil.NoSuchProcess:
        return False


def process_is_running(state):
    process = _verified_process(state)
    return process is not None and _is_live(process)


def _live_group_members(group):
    members = {}
    for candidate in psutil.process_iter():
        try:
            process = psutil.Process(candidate.pid)
            if os.getpgid(process.pid) == group and _is_live(process):
                members[process.pid] = process
        except (psutil.NoSuchProcess, ProcessLookupError):
            continue
    return members


def _stop_group(state):
    pid = state['pid']
    if pid <= 0:
        raise RuntimeError('Invalid mock-server PID')
    if 'create_time' not in state:
        return
    if _verified_process(state) is None:
        if not psutil.pid_exists(pid) and _live_group_members(pid):
            raise RuntimeError(f'Mock-server leader {pid} is gone but its group is occupied; state retained')
        return

    known = _live_group_members(pid)
    if known and _verified_process(state) is None:
        raise RuntimeError(f'Mock-server group {pid} changed before cleanup; state retained')
    for sig in (signal.SIGTERM, signal.SIGKILL):
        signaled = set()
        deadline = time.monotonic() + STOP_TIMEOUT
        while True:
            live = {}
            for process in known.values():
                try:
                    if _is_live(process) and os.getpgid(process.pid) == pid:
                        live[process.pid] = process
                except ProcessLookupError:
                    pass
            if live or _verified_process(state) is not None:
                live.update(_live_group_members(pid))
            elif _live_group_members(pid):
                raise RuntimeError(f'Mock-server group {pid} can no longer be verified; state retained')
            if not live:
                return
            known.update(live)
            for process in live.values():
                identity = (process.pid, process.create_time())
                if identity not in signaled:
                    try:
                        process.send_signal(sig)
                    except psutil.NoSuchProcess:
                        pass
                    signaled.add(identity)
            if time.monotonic() >= deadline:
                break
            time.sleep(0.05)
    raise RuntimeError(f'Mock-server group {pid} did not stop; state retained')


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
        if state and process_is_running(state):
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
                process = psutil.Popen(
                    [python, 'app.py', '--addr', host, '--port', str(port)],
                    cwd=__mock_server_root__, stdout=log, stderr=subprocess.STDOUT,
                    stdin=subprocess.DEVNULL, start_new_session=True,
                )
            self._write_state({'pid': process.pid, 'create_time': process.create_time(),
                               'port': port, 'endpoint': self.endpoint})
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
                self.finish(process)
            raise
        print(f'Integration mock server ready at {self.endpoint}', flush=True)
        return process

    def finish(self, process=None):
        """Stop the recorded process group, including reloader workers."""
        state = self._read_state()
        if state is None and process is None:
            return
        identity = {'pid': process.pid, 'create_time': process.create_time()} if process is not None else state
        _stop_group(identity)
        if process is not None:
            process.wait(timeout=STOP_TIMEOUT)
        if (state is not None and state['pid'] == identity['pid'] and
                state.get('create_time') == identity.get('create_time') and self._read_state() == state):
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
