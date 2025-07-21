"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import pytest

from .sdkmanager import _parse_sdkmanager_list_output, AndroidPackage

__output__ = '''Loading package information...
Loading local repository...
[=========                              ] 25% Loading local repository...
[==========================             ] 66% Fetch remote repository...
[=======================================] 100% Computing updates...
Installed packages:
  Path                                                     | Version       | Description                             | Location
  -------                                                  | -------       | -------                                 | -------
  build-tools;35.0.0                                       | 35.0.0        | Android SDK Build-Tools 35              | build-tools/35.0.0
  cmdline-tools;latest                                     | 19.0          | Android SDK Command-line Tools (latest) | cmdline-tools/latest
  emulator                                                 | 35.5.10       | Android Emulator                        | emulator
  ndk;28.0.13004108                                        | 28.0.13004108 | NDK (Side by side) 28.0.13004108        | ndk/28.0.13004108
  platforms;android-36                                     | 2             | Android SDK Platform 36                 | platforms/android-36
  system-images;android-36;google_apis_playstore;arm64-v8a | 6             | Google Play ARM 64 v8a System Image     | system-images/android-36/google_apis_playstore/arm64-v8a

Available Packages:
  Path                                                                            | Version           | Description
  -------                                                                         | -------           | -------
  add-ons;addon-google_apis-google-22                                             | 1                 | Google APIs
  add-ons;addon-google_apis-google-23                                             | 1                 | Google APIs
  add-ons;addon-google_apis-google-24                                             | 1                 | Google APIs
  build-tools;19.1.0                                                              | 19.1.0            | Android SDK Build-Tools 19.1
  build-tools;20.0.0                                                              | 20.0.0            | Android SDK Build-Tools 20
  build-tools;21.1.2                                                              | 21.1.2            | Android SDK Build-Tools 21.1.2
  system-images;android-Baklava;google_apis_ps16k;x86_64                          | 4                 | Pre-Release 16 KB Page Size Google APIs Intel x86_64 Atom System Image

Available Updates:
  ID             | Installed | Available
  -------        | -------   | -------
  emulator       | 35.5.10   | 35.6.11
  platform-tools | 35.0.2    | 36.0.0
'''


def test_parse_sdkmanager_list_output():
    got = _parse_sdkmanager_list_output(__output__)
    assert got == [
        AndroidPackage(path='build-tools;35.0.0', version='35.0.0', description='Android SDK Build-Tools 35'),
        AndroidPackage(path='cmdline-tools;latest', version='19.0', description='Android SDK Command-line Tools (latest)'),
        AndroidPackage(path='emulator', version='35.5.10', description='Android Emulator'),
        AndroidPackage(path='ndk;28.0.13004108', version='28.0.13004108', description='NDK (Side by side) 28.0.13004108'),
        AndroidPackage(path='platforms;android-36', version='2', description='Android SDK Platform 36'),
        AndroidPackage(path='system-images;android-36;google_apis_playstore;arm64-v8a', version='6', description='Google Play ARM 64 v8a System Image'),
    ]
