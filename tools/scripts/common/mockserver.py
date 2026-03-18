"""
Utility code for running the Flask app in tools/mock_server, which manages its Python
environment separately from tools/scripts and which therefore must be run in its own
Python interpreter.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import subprocess
import signal
import socket
import time
from contextlib import contextmanager
from typing import Generator

from common.log import get_default_logger

__mock_server_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', 'mock_server'))


def prepare_mock_server_venv():
    log = get_default_logger()

    # mock_server has its own requirements.txt file, so make sure it has a venv dir
    venv_dir = os.path.join(__mock_server_root__, 'venv')
    if os.path.isdir(venv_dir):
        log.info(f'venv exists: {venv_dir}')
    else:
        log.info(f'Initializing venv at: {venv_dir}')
        subprocess.check_call(['python3', '-m', 'venv', venv_dir])
    venv_python = os.path.join(venv_dir, 'bin', 'python')

    # Ensure that we have the latest dependencies installed to that venv
    log.info('Installing dependencies with pip...')
    requirements_txt = os.path.join(__mock_server_root__, 'requirements.txt')
    subprocess.check_call([venv_python, '-m', 'pip', 'install', '-r', requirements_txt])
    log.info('Dependencies up to date.')

    # Run app.py --update-schemas to ensure we have the latest RUM events schemas.
    # Note: schema_update.py has no __main__ block and is a no-op when run directly.
    app_py = os.path.join(__mock_server_root__, 'app.py')
    subprocess.check_call([venv_python, app_py, '--update-schemas'], cwd=__mock_server_root__)
    log.info('Event schemas up to date.')


@contextmanager
def run_mock_server(bind_addr: str, port: int) -> Generator[None, None, None]:
    log = get_default_logger()

    venv_python = os.path.join(__mock_server_root__, 'venv', 'bin', 'python')
    # Bind to 0.0.0.0 so the server accepts connections on all interfaces, including
    # 10.0.2.2 (Android emulator's alias for the host). This allows integration tests to
    # make non-first-party requests via 10.0.2.2 while first-party hosts are configured
    # only on the LAN IP, so the RUM SDK treats the two addresses differently.
    args = [venv_python, 'app.py', '--addr', '0.0.0.0', '--port', str(port)]

    process = subprocess.Popen(args, cwd=__mock_server_root__)

    # Wait for the server to be ready before yielding
    deadline = time.time() + 30
    while time.time() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f'Mock server exited unexpectedly with code {process.returncode}')
        try:
            with socket.create_connection((bind_addr, port), timeout=1):
                break
        except OSError:
            time.sleep(0.5)
    else:
        process.kill()
        raise RuntimeError(f'Mock server did not start within 30 seconds on {bind_addr}:{port}')

    log.info(f'Mock server is ready at http://{bind_addr}:{port}')

    try:
        yield
    finally:
        log.info('Shutting down mock server...')
        try:
            process.send_signal(signal.SIGINT)
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            log.warning('Mock server did not shut down gracefully, forcing kill...')
            process.kill()
            process.wait()
        log.info(f'Mock server exited with code {process.returncode}')
