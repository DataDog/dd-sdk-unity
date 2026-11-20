# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

from dd_sdk_unity.scripts.common.versions.sdkversions import SdkVersionTable, SdkVersionTableRow
from dd_sdk_unity.scripts.common.versions.semver import Version

__initial_table__ = '''| Unity SDK | iOS SDK | Android SDK | C++ SDK |
|----------|--------|------------|---------|
| 1.3.0 | 2.17.0 | 2.14.0 | — |
| 1.2.0 | 2.14.1 | 2.11.0 | — |
| 1.0.4 | 2.7.1 | 2.6.2 | — |
| 1.0.0 | develop@e59ba8 | 2.5.0 | — |'''

__new_unity_version__ = Version.parse('1.4.0')
__new_ios_version__ = Version.parse('2.28.1')
__new_android_version__ = Version.parse('2.22.0')
__new_cpp_version__ = Version.parse('0.9.0')

__new_table__ = '''| Unity SDK | iOS SDK        | Android SDK | C++ SDK |
|-----------|----------------|-------------|---------|
| 1.4.0     | 2.28.1         | 2.22.0      | 0.9.0   |
| 1.3.0     | 2.17.0         | 2.14.0      | —       |
| 1.2.0     | 2.14.1         | 2.11.0      | —       |
| 1.0.4     | 2.7.1          | 2.6.2       | —       |
| 1.0.0     | develop@e59ba8 | 2.5.0       | —       |
'''


def test_SdkVersionTable():
    table = SdkVersionTable.parse(__initial_table__)
    assert table == SdkVersionTable(
        rows=[
            SdkVersionTableRow(Version.parse('1.3.0'), '2.17.0', '2.14.0', None),
            SdkVersionTableRow(Version.parse('1.2.0'), '2.14.1', '2.11.0', None),
            SdkVersionTableRow(Version.parse('1.0.4'), '2.7.1', '2.6.2', None),
            SdkVersionTableRow(Version.parse('1.0.0'), 'develop@e59ba8', '2.5.0', None),
        ]
    )

    table.set(
        __new_unity_version__, __new_ios_version__, __new_android_version__, __new_cpp_version__,
    )
    assert table.render() == __new_table__
