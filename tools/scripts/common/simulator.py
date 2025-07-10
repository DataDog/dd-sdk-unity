from contextlib import contextmanager

from common.android import AndroidDeviceSpec, run_android_device
from common.apple import AppleDeviceSpec, run_apple_device


__default_ios_device__ = AppleDeviceSpec('iOS 18.5', 'iPhone 15 Pro')
__default_android_device__ = AndroidDeviceSpec.default(api_level=33, device='pixel_4')


@contextmanager
def run_default_simulator(platform: str):
    if platform.lower() == 'ios':
        with run_apple_device(__default_ios_device__):
            yield
    else:
        assert platform.lower() == 'android'
        with run_android_device(__default_android_device__):
            yield
