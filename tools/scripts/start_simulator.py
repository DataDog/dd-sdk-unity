"""
Starts an emulated device for either iOS or Android, blocking until Ctrl-C is pressed.
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
