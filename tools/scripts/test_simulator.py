import sys
import time
import argparse
from contextlib import contextmanager

from common.log import init_logger
from common.android import AndroidDeviceSpec, run_android_device
from common.apple import AppleDeviceSpec, run_apple_device


__default_ios_device__ = AppleDeviceSpec('iOS 18.5', 'iPhone 15 Pro')
__default_android_device__ = AndroidDeviceSpec.default(api_level=32, device='pixel_4')


@contextmanager
def _run_default_simulator(platform: str):
    if platform.lower() == 'ios':
        with run_apple_device(__default_ios_device__):
            yield
    else:
        assert platform.lower() == 'android'
        with run_android_device(__default_android_device__):
            yield


def test_simulator(platform: str) -> int:
    log = init_logger()
    with _run_default_simulator(platform.lower()):
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

    sys.exit(test_simulator(args.platform))
