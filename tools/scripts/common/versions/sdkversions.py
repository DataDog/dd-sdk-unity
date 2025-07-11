"""
Utility code for modifying NATIVE_SDK_VERSIONS.md, which records for posterity the
canonical versions of dd-sdk-android and dd-sdk-ios that are included in each Unity SDK
release.
"""
from dataclasses import dataclass
from typing import List

from .semver import Version


__heading_unity__ = 'Unity SDK'
__heading_ios__ = 'iOS SDK'
__heading_android__ = 'Android SDK'


@dataclass
class SdkVersionTableRow:
    unity_version: Version
    ios_version_str: str
    android_version_str: str


@dataclass
class SdkVersionTable:
    rows: List[SdkVersionTableRow]

    def set(self, unity_version: Version, ios_version: Version, android_version: Version):
        row = SdkVersionTableRow(
            unity_version=unity_version,
            ios_version_str=str(ios_version),
            android_version_str=str(android_version)
        )
        row_index = next((i for i, x in enumerate(self.rows) if x.unity_version == unity_version), -1)
        if row_index >= 0:
            self.rows[row_index] = row
        else:
            self.rows.insert(0, row)

    def render(self) -> str:
        pad_a = max([len(str(row.unity_version)) for row in self.rows] + [len(__heading_unity__)])
        pad_b = max([len(row.ios_version_str) for row in self.rows] + [len(__heading_ios__)])
        pad_c = max([len(row.android_version_str) for row in self.rows] + [len(__heading_android__)])

        def _render_line(a: str, b: str, c: str) -> str:
            return f'| {a.ljust(pad_a)} | {b.ljust(pad_b)} | {c.ljust(pad_c)} |'

        lines: List[str] = [
            _render_line(__heading_unity__, __heading_ios__, __heading_android__),
            '|' + ('-' * (pad_a + 2)) + '|' + ('-' * (pad_b + 2)) + '|' + ('-' * (pad_c + 2)) + '|',
        ]
        for row in self.rows:
            lines.append(_render_line(str(row.unity_version), row.ios_version_str, row.android_version_str))

        return '\n'.join(lines) + '\n'


    @classmethod
    def parse(cls, text: str) -> 'SdkVersionTable':
        rows: List[SdkVersionTableRow] = []
        seen_headings = False
        seen_border = False
        for line in text.splitlines():
            line = line.strip()
            if line.count('|') == 4 and line[0] == '|' and line[-1] == '|':
                a, b, c = [s.strip() for s in line[1:-1].split('|')]
                if not seen_headings:
                    if (a, b, c) != (__heading_unity__, __heading_ios__, __heading_android__):
                        raise ValueError(f"Invalid first line in SDK version table: '{line}' (expected headings)")
                    seen_headings = True
                    continue
                if not seen_border:
                    if not all(s.count('-') == len(s) for s in (a, b, c)):
                        raise ValueError(f"Invalid second line in SDK version table: '{line}' (expected border)")
                    seen_border = True
                    continue

                rows.append(SdkVersionTableRow(
                    unity_version=Version.parse(a),
                    ios_version_str=b,
                    android_version_str=c,
                ))
        return SdkVersionTable(rows=rows)
