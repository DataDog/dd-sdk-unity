import os
import sys
import argparse
import subprocess
import signal
import time
from typing import List

from common.log import init_logger
from common.mockserver import prepare_mock_server_venv, run_mock_server

__mock_server_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'mock_server'))


def init_mock_server(start: bool, port: int):
    init_logger()
    prepare_mock_server_venv()
    if start:
        with run_mock_server(port, prefer_localhost=True) as mock:
            while True:
                try:
                    time.sleep(0)
                except KeyboardInterrupt:
                    break

            mock.get()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Ensures that mock_server has all required schemas along with a properly initialized Python venv')
    parser.add_argument('--start', action='store_true', help='If true, this script will start the mock server and block')
    parser.add_argument('--port', type=int, default=5000)
    args = parser.parse_args()

    sys.exit(init_mock_server(args.start, args.port))
