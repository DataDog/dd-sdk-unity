import pytest

from .sdkversions import SdkVersionTable, SdkVersionTableRow
from .semver import Version


__old_table__ = '''| Unity SDK | iOS SDK | Android SDK |
|----------|--------|------------|
| 1.3.0 | 2.17.0 | 2.14.0 |
| 1.2.0 | 2.14.1 | 2.11.0 |
| 1.0.4 | 2.7.1 | 2.6.2 |
| 1.0.0 | develop@e59ba8 | 2.5.0 |'''

__new_unity_version__ = Version.parse('1.4.0')
__new_ios_version__ = Version.parse('2.28.1')
__new_android_version__ = Version.parse('2.22.0')

__new_table__ = '''| Unity SDK | iOS SDK        | Android SDK |
|-----------|----------------|-------------|
| 1.4.0     | 2.28.1         | 2.22.0      |
| 1.3.0     | 2.17.0         | 2.14.0      |
| 1.2.0     | 2.14.1         | 2.11.0      |
| 1.0.4     | 2.7.1          | 2.6.2       |
| 1.0.0     | develop@e59ba8 | 2.5.0       |
'''


def test_SdkVersionTable():
    table = SdkVersionTable.parse(__old_table__)
    assert table == SdkVersionTable(
        rows=[
            SdkVersionTableRow(Version.parse('1.3.0'), '2.17.0', '2.14.0'),
            SdkVersionTableRow(Version.parse('1.2.0'), '2.14.1', '2.11.0'),
            SdkVersionTableRow(Version.parse('1.0.4'), '2.7.1', '2.6.2'),
            SdkVersionTableRow(Version.parse('1.0.0'), 'develop@e59ba8', '2.5.0'),
        ]
    )

    table.set(__new_unity_version__, __new_ios_version__, __new_android_version__)
    assert table.render() == __new_table__
