"""
Ensures that the mock server Flask app is prepared to run, and optionally runs it,
blocking until Ctrl-C is received.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import sys
import argparse
import time

from common.log import init_logger
from common.mockserver import prepare_mock_server_venv, run_mock_server


def init_mock_server(start: bool, port: int, addr: str):
    init_logger()
    prepare_mock_server_venv()
    if start:
        with run_mock_server(addr, port):
            while True:
                try:
                    time.sleep(0)
                except KeyboardInterrupt:
                    break


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Ensures that mock_server has all required schemas along with a properly initialized Python venv')
    parser.add_argument('--start', action='store_true', help='If true, this script will start the mock server and block')
    parser.add_argument('--port', type=int, default=5000, help='Port on which mock server will listen for HTTP connections')
    parser.add_argument('--addr', type=str, default='', help='Address which mock server will bind to; omit to auto-resolve private IP')
    args = parser.parse_args()

    sys.exit(init_mock_server(args.start, args.port, args.addr))
