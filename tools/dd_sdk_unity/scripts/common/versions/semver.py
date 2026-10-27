# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for semantic version numbers.
"""
import re
from dataclasses import dataclass
from enum import Enum
from functools import total_ordering

_NUMBER = r"(?:0|[1-9][0-9]*)"
_PRERELEASE = rf"(?:{_NUMBER}|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"
VERSION_PATTERN = (
    rf"{_NUMBER}\.{_NUMBER}\.{_NUMBER}"
    rf"(?:-{_PRERELEASE}(?:\.{_PRERELEASE})*)?"
    r"(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?"
)


class VersionBump(Enum):
    PATCH = 1
    MINOR = 2
    MAJOR = 3

    def __str__(self) -> str:
        return {
            1: 'patch',
            2: 'minor',
            3: 'major',
        }[self.value]

    def __lt__(self, other: VersionBump) -> bool:
        return self.value < other.value


@total_ordering
@dataclass
class Version:
    """A SemVer version; build metadata is preserved but ignored in comparisons."""
    major: int
    minor: int
    patch: int
    prerelease: str = ""
    build: str = ""

    @property
    def core(self) -> str:
        return f'{self.major:d}.{self.minor:d}.{self.patch:d}'

    def __str__(self) -> str:
        suffix = f"-{self.prerelease}" if self.prerelease else ""
        metadata = f"+{self.build}" if self.build else ""
        return f"{self.core}{suffix}{metadata}"
    
    def __eq__(self, other: object) -> bool:
        if isinstance(other, str):
            return str(self) == other
        if not isinstance(other, Version):
            return NotImplemented
        lhs = (self.major, self.minor, self.patch, self.prerelease)
        rhs = (other.major, other.minor, other.patch, other.prerelease)
        return lhs == rhs

    def __lt__(self, other: Version) -> bool:
        lhs = (self.major, self.minor, self.patch)
        rhs = (other.major, other.minor, other.patch)
        if lhs != rhs:
            return lhs < rhs
        if not self.prerelease:
            return False
        if not other.prerelease:
            return True

        left = self.prerelease.split(".")
        right = other.prerelease.split(".")
        for first, second in zip(left, right):
            if first == second:
                continue
            if first.isdigit() and second.isdigit():
                return int(first) < int(second)
            if first.isdigit() != second.isdigit():
                return first.isdigit()
            return first < second
        return len(left) < len(right)
    
    def bump(self, type: VersionBump) -> Version:
        if type == VersionBump.MAJOR:
            return Version(self.major + 1, 0, 0)
        if type == VersionBump.MINOR:
            return Version(self.major, self.minor + 1, 0)
        if type == VersionBump.PATCH:
            return Version(self.major, self.minor, self.patch + 1)
        raise ValueError(f'Unexpected version bump type: {type}')

    @classmethod
    def parse(cls, s: str) -> Version:
        if not re.fullmatch(VERSION_PATTERN, s):
            raise ValueError(f'Expected a SemVer version, got {s!r}')
        version, _, build = s.partition("+")
        core, _, prerelease = version.partition("-")
        major, minor, patch = core.split(".")
        return cls(
            major=int(major),
            minor=int(minor),
            patch=int(patch),
            prerelease=prerelease,
            build=build,
        )
