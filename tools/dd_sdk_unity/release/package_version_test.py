# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

import io
import json

from dd_sdk_unity.release.package_version import _modify_package_json_impl, merge_package_version
from dd_sdk_unity.scripts.common.versions.semver import Version

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
    "com.unity.nuget.newtonsoft-json": "3.2.1"
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
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
'''


def test_modify_package_json_impl():
    infile = io.StringIO(__old_package_json__)
    got = _modify_package_json_impl(infile, __new_version__)
    assert got == __new_package_json__


def test_package_integration_keeps_higher_version_and_other_development_fields():
    development = '{"version":"3.10.0","dependencies":{"new":"1.0.0"},"displayName":"Current"}\n'
    assert merge_package_version(development, "3.9.1") == development
    assert merge_package_version(development, "3.10.0") == development
    updated = json.loads(merge_package_version(development, "3.11.0"))
    assert updated == {**json.loads(development), "version": "3.11.0"}
