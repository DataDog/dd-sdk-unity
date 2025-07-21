"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import pytest

import io

from .package import _modify_package_json_impl
from .semver import Version


__old_package_json__ = '''{
  "name": "com.datadoghq.unity",
  "version": "0.8.0",
  "displayName": "Datadog Unity",
  "description": "Datadog Plugin for Unity",
  "unity": "2022.3",
  "license": "Apache 2.0",
  "keywords": [
    "logging",
    "diagnostics"
  ],
  "type": "library",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1",
    "com.google.external-dependency-manager": "1.2.178"
  }
}
'''

__new_version__ = Version.parse('1.5.1')

__new_package_json__ = '''{
  "name": "com.datadoghq.unity",
  "version": "1.5.1",
  "displayName": "Datadog Unity",
  "description": "Datadog Plugin for Unity",
  "unity": "2022.3",
  "license": "Apache 2.0",
  "keywords": [
    "logging",
    "diagnostics"
  ],
  "type": "library",
  "dependencies": {
    "com.unity.nuget.newtonsoft-json": "3.2.1",
    "com.google.external-dependency-manager": "1.2.178"
  }
}
'''


def test_modify_package_json_impl():
    infile = io.StringIO(__old_package_json__)
    got = _modify_package_json_impl(infile, __new_version__)
    assert got == __new_package_json__
