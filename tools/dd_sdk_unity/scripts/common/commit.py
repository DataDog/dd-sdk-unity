# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for parsing convention commits.
"""
import re
from dataclasses import dataclass

from dd_sdk_unity.scripts.common.versions import VersionBump

__conventional_commit_pattern__ = re.compile(r'^([a-z]+)(?:\((.*)\))?(\!)?: (.*)$')
__refs_pattern__ = re.compile(r'')


@dataclass
class ConventionalCommitInfo:
    type: str
    scope: str
    bang: bool

    @classmethod
    def parse(cls, headline: str) -> tuple[ConventionalCommitInfo | None, str]:
        match = __conventional_commit_pattern__.match(headline)
        if not match:
            return None, headline
        return cls(
            type=match.group(1) or '',
            scope=match.group(2) or '',
            bang=match.group(3) == '!',
        ), match.group(4)


@dataclass
class CommitInfo:
    conventional: ConventionalCommitInfo | None
    headline: str
    body: str
    bump: VersionBump
    refs: set[str]

    @classmethod
    def parse(cls, commit_message: str) -> CommitInfo:
        lines = commit_message.splitlines()
        headline, tail_lines = lines[0], lines[1:]
        conventional, headline = ConventionalCommitInfo.parse(headline)
        
        body = ''
        first_non_blank_tail_line_index = next((i for i, s in enumerate(tail_lines) if s.strip() != ''), -1)
        if first_non_blank_tail_line_index >= 0:
            body = '\n'.join(tail_lines[first_non_blank_tail_line_index:]) + '\n'

        bump = VersionBump.PATCH
        if conventional:
            if conventional.type == 'feat':
                bump = max(bump, VersionBump.MINOR)
            if conventional.bang:
                bump = VersionBump.MAJOR
        if 'BREAKING' in headline or 'BREAKING' in body:
            bump = VersionBump.MAJOR

        refs: set[str] = set()
        for line in lines:
            if line.lower().startswith('refs:'):
                refs_str = line[len('refs:'):]
                refs_values = [s.strip() for s in refs_str.split(',') if s.strip() != '']
                for ref in refs_values:
                    refs.add(ref)

        return cls(
            conventional=conventional,
            headline=headline,
            body=body,
            bump=bump,
            refs=refs,
        )
