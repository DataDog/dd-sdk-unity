# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for semantic version numbers.
"""
import re
from dataclasses import dataclass


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
        return f'{self.major:d}.{self.minor:d}.{self.patch:d}'
    
    def __eq__(self, other: object) -> bool:
        if isinstance(other, str):
            return str(self) == other
        if not isinstance(other, Version):
            return NotImplemented
        lhs = (self.major, self.minor, self.patch)
        rhs = (other.major, other.minor, other.patch)
        return lhs == rhs

    def __lt__(self, other: Version) -> bool:
        lhs = (self.major, self.minor, self.patch)
        rhs = (other.major, other.minor, other.patch)
        return lhs < rhs
    
    @classmethod
    def parse(cls, s: str) -> Version:
        match = re.match(r'^([0-9]+)\.([0-9]+)\.([0-9]+)$', s)
        if not match:
            raise ValueError(f'Unexpected format for version string: {s}')
        return cls(
            major=int(match.group(1)),
            minor=int(match.group(2)),
            patch=int(match.group(3)),
        )
