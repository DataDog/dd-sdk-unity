"""
Starts an emulated device for either iOS or Android, blocking until Ctrl-C is pressed.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import sys
import time
import argparse

from common.log import init_logger
from common.simulator import run_default_simulator


def start_simulator(platform: str) -> int:
    log = init_logger()
    with run_default_simulator(platform.lower()):
        log.info('Simulator running.')
        log.info('Press Ctrl-C to exit...')
        while True:
            try:
                time.sleep(0)
            except KeyboardInterrupt:
                log.info('')
                break
    return 0


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='Boots up an emulated mobile device for testing')
    parser.add_argument('--platform', choices=['ios', 'android'], required=True)
    args = parser.parse_args()

    sys.exit(start_simulator(args.platform))
