import pytest

from .avdmanager import _parse_list_avd_output, Avd

__output__ = '''Available Android Virtual Devices:
    Name: foo-twenty-three
    Path: /Users/someone/.android/avd/twenty-three.avd
  Target: Google APIs (Google Inc.)
          Based on: Android 6.0 ("Marshmallow") Tag/ABI: google_apis/arm64-v8a
  Sdcard: 512 MB
---------
    Name: Medium_Phone_API_36.0
  Device: medium_phone (Generic)
    Path: /Users/someone/.android/avd/Medium_Phone.avd
  Target: Google Play (Google Inc.)
          Based on: Android API 36 Tag/ABI: google_apis_playstore/arm64-v8a
    Skin: 1080x2400
  Sdcard: 512M
---------
    Name: pixel-four
  Device: pixel_4 (Google)
    Path: /Users/someone/.android/avd/pixel-four.avd
  Target: Google APIs (Google Inc.)
          Based on: Android 12L ("Sv2") Tag/ABI: google_apis/arm64-v8a
  Sdcard: 512 MB
'''

__output_empty__ = '''Available Android Virtual Devices:
'''


def test_parse_avd_list_output():
    got = _parse_list_avd_output(__output__)
    assert got == [
        Avd(
            name='foo-twenty-three',
            device=None,
            path='/Users/someone/.android/avd/twenty-three.avd',
            api_level=23,
            tag='google_apis',
            abi='arm64-v8a',
            skin=None,
            sdcard='512 MB',
        ),
        Avd(
            name='Medium_Phone_API_36.0',
            device='medium_phone',
            path='/Users/someone/.android/avd/Medium_Phone.avd',
            api_level=36,
            tag='google_apis_playstore',
            abi='arm64-v8a',
            skin='1080x2400',
            sdcard='512M',
        ),
        Avd(
            name='pixel-four',
            device='pixel_4',
            path='/Users/someone/.android/avd/pixel-four.avd',
            api_level=32,
            tag='google_apis',
            abi='arm64-v8a',
            skin=None,
            sdcard='512 MB',
        ),
    ]
    assert _parse_list_avd_output(__output_empty__) == []
