# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for modifying NATIVE_SDK_VERSIONS.md, which records for posterity the
canonical versions of dd-sdk-android, dd-sdk-ios, and dd-sdk-cpp included in each Unity SDK
release.
"""
from dataclasses import dataclass

from dd_sdk_unity.scripts.common.versions.semver import Version

HEADINGS = ('Unity SDK', 'iOS SDK', 'Android SDK', 'C++ SDK')


@dataclass
class SdkVersionTableRow:
    unity_version: Version
    ios_version_str: str | None
    android_version_str: str | None
    cpp_version_str: str | None


@dataclass
class SdkVersionTable:
    rows: list[SdkVersionTableRow]

    def set(
        self, unity_version: Version, ios_version: Version, android_version: Version,
        cpp_version: Version,
    ) -> None:
        row = SdkVersionTableRow(
            unity_version=unity_version,
            ios_version_str=str(ios_version),
            android_version_str=str(android_version),
            cpp_version_str=str(cpp_version),
        )
        row_index = next((i for i, x in enumerate(self.rows) if x.unity_version == unity_version), -1)
        if row_index >= 0:
            self.rows[row_index] = row
        else:
            self.rows.insert(0, row)

    def render(self) -> str:
        values = [
            (str(row.unity_version), row.ios_version_str or '—',
             row.android_version_str or '—', row.cpp_version_str or '—')
            for row in self.rows
        ]
        widths = [max(map(len, column)) for column in zip(HEADINGS, *values)]

        def _render_line(cells: tuple[str, ...]) -> str:
            return '| ' + ' | '.join(cell.ljust(width) for cell, width in zip(cells, widths)) + ' |'

        lines = [
            _render_line(HEADINGS),
            '|' + '|'.join('-' * (width + 2) for width in widths) + '|',
            *(_render_line(row) for row in values),
        ]

        return '\n'.join(lines) + '\n'


    @classmethod
    def parse(cls, text: str) -> SdkVersionTable:
        header, border, *rows = [
            tuple(cell.strip() for cell in line.strip().strip('|').split('|'))
            for line in text.splitlines() if line.strip()
        ]
        if header != HEADINGS:
            raise ValueError('Invalid SDK version table headings')
        if len(border) != len(HEADINGS) or not all(cell and set(cell) == {'-'} for cell in border):
            raise ValueError('Invalid SDK version table border')
        return cls(rows=[
            SdkVersionTableRow(
                unity_version=Version.parse(unity),
                ios_version_str=None if ios == '—' else ios,
                android_version_str=None if android == '—' else android,
                cpp_version_str=None if cpp == '—' else cpp,
            )
            for unity, ios, android, cpp in rows
        ])
