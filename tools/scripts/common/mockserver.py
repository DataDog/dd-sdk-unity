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

    # Run app.py --update-schemas to ensure we have the latest RUM events schemas
    update_schema_args = [venv_python, 'app.py', '--update-schemas']
    subprocess.check_call(update_schema_args, cwd=__mock_server_root__)
    log.info('Event schemas up to date.')


@contextmanager
def run_mock_server(bind_addr: str, port: int) -> Generator[None, None, None]:
    log = get_default_logger()

    venv_python = os.path.join(__mock_server_root__, 'venv', 'bin', 'python')
    args = [venv_python, 'app.py', '--addr', bind_addr, '--port', str(port)]

    process = subprocess.Popen(args, cwd=__mock_server_root__)
    
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
