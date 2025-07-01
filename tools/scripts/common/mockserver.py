import os
import subprocess
import signal
from contextlib import contextmanager
from typing import Generator

from pydantic import BaseModel

from common.log import get_default_logger

__mock_server_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', 'mock_server'))


class MockServerClient:
    url: str

    def __init__(self, url: str):
        self.url = url

    def get(self):
        print(self.url)



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

    # Run mock_server/schema_update.py to ensure we have the latest RUM events schemas
    schema_update_py = os.path.join(__mock_server_root__, 'schema_update.py')
    subprocess.check_call([venv_python, schema_update_py])
    log.info('Event schemas up to date.')


@contextmanager
def run_mock_server(port: int, prefer_localhost: bool) -> Generator[MockServerClient, None, None]:
    log = get_default_logger()

    venv_python = os.path.join(__mock_server_root__, 'venv', 'bin', 'python')
    args = [venv_python, 'app.py', '--port', str(port)]
    if prefer_localhost:
        args.append('--prefer-localhost')

    process = subprocess.Popen(args, cwd=__mock_server_root__)
    client = MockServerClient(f'http://127.0.0.1:{port}')
    
    try:
        yield client
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
