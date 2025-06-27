import re
from enum import Enum
from dataclasses import dataclass
from typing import Any


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

    def __lt__(self, other: 'VersionBump') -> bool:
        return self.value < other.value


@dataclass
class Version:
    """
    Represents a release of a Datadog SDK, which is canonically formatted '%d.%d.%d';
    no additional suffixes, no 'v' prefix, e.g. '1.3.2'. This format is used in exactly
    this format for both git tags and GitHub releases.
    """
    major: int
    minor: int
    patch: int

    def __str__(self) -> str:
        return '%d.%d.%d' % (self.major, self.minor, self.patch)
    
    def __eq__(self, other: Any) -> bool:
        if isinstance(other, str):
            return str(self) == other
        if not isinstance(other, Version):
            return NotImplemented
        lhs = (self.major, self.minor, self.patch)
        rhs = (other.major, other.minor, other.patch)
        return lhs == rhs

    def __lt__(self, other: 'Version') -> bool:
        lhs = (self.major, self.minor, self.patch)
        rhs = (other.major, other.minor, other.patch)
        return lhs < rhs
    
    def bump(self, type: VersionBump) -> 'Version':
        if type == VersionBump.MAJOR:
            return Version(self.major + 1, 0, 0)
        if type == VersionBump.MINOR:
            return Version(self.major, self.minor + 1, 0)
        if type == VersionBump.PATCH:
            return Version(self.major, self.minor, self.patch + 1)
        raise ValueError(f'Unexpected version bump type: {type}')

    @classmethod
    def parse(cls, s: str) -> 'Version':
        match = re.match(r'^([0-9]+)\.([0-9]+)\.([0-9]+)$', s)
        if not match:
            raise ValueError(f'Unexpected format for version string: {s}')
        return cls(
            major=int(match.group(1)),
            minor=int(match.group(2)),
            patch=int(match.group(3)),
        )
