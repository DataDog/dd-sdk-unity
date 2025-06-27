import re
from dataclasses import dataclass
from typing import Optional, Tuple

from common.versions import VersionBump


__conventional_commit_pattern__ = re.compile(r'^([a-z]+)(?:\((.*)\))?(\!)?: (.*)$')


@dataclass
class ConventionalCommitInfo:
    type: str
    scope: str
    bang: bool

    @classmethod
    def parse(cls, headline) -> Tuple[Optional['ConventionalCommitInfo'], str]:
        match = __conventional_commit_pattern__.match(headline)
        if not match:
            return None, headline
        return cls(
            type=match.group(1),
            scope=match.group(2),
            bang=match.group(3) == '!',
        ), match.group(4)


@dataclass
class CommitInfo:
    conventional: Optional[ConventionalCommitInfo]
    headline: str
    body: str
    bump: VersionBump

    @classmethod
    def parse(cls, commit_message: str) -> 'CommitInfo':
        lines = commit_message.splitlines()
        headline, tail_lines = lines[0], lines[1:]
        conventional, headline = ConventionalCommitInfo.parse(headline)
        body = '\n'.join(tail_lines).strip() + '\n'

        bump = VersionBump.PATCH
        if conventional:
            if conventional.type == 'feat':
                bump = max(bump, VersionBump.MINOR)
            if conventional.bang:
                bump = VersionBump.MAJOR
        if 'BREAKING' in headline or 'BREAKING' in body:
            bump = VersionBump.MAJOR

        return cls(
            conventional=conventional,
            headline=headline,
            body=body,
            bump=bump,
        )
