import pytest

from .commit import CommitInfo, ConventionalCommitInfo
from .versions import VersionBump


def test_CommitInfo_parse():
    for message, want in [
        (
            'fix a bug or something',
            CommitInfo(
                conventional=None,
                headline='fix a bug or something',
                body='',
                bump=VersionBump.PATCH,
                refs=set(),
            ),
        ),
        (
            'fix: Ensure rate-limiting is properly applied',
            CommitInfo(
                conventional=ConventionalCommitInfo(
                    type='fix',
                    scope='',
                    bang=False,
                ),
                headline='Ensure rate-limiting is properly applied',
                body='',
                bump=VersionBump.PATCH,
                refs=set(),
            ),
        ),
        (
            'feat: Do something very important\nImportant things are good to do.\n',
            CommitInfo(
                conventional=ConventionalCommitInfo(
                    type='feat',
                    scope='',
                    bang=False,
                ),
                headline='Do something very important',
                body='Important things are good to do.\n',
                bump=VersionBump.MINOR,
                refs=set(),
            ),
        ),
        (
            'feat(bigthing)!: Very big thing',
            CommitInfo(
                conventional=ConventionalCommitInfo(
                    type='feat',
                    scope='bigthing',
                    bang=True,
                ),
                headline='Very big thing',
                body='',
                bump=VersionBump.MAJOR,
                refs=set(),
            ),
        ),
        (
            'chore(ci): Kick CI for major release\n\nThis is a BREAKING CHANGE, whoa',
            CommitInfo(
                conventional=ConventionalCommitInfo(
                    type='chore',
                    scope='ci',
                    bang=False,
                ),
                headline='Kick CI for major release',
                body='This is a BREAKING CHANGE, whoa\n',
                bump=VersionBump.MAJOR,
                refs=set(),
            ),
        ),
        (
            'fix: Make that bug go away\n\nWe did it.\n\nrefs: ABC-123,  #33 \n',
            CommitInfo(
                conventional=ConventionalCommitInfo(
                    type='fix',
                    scope='',
                    bang=False,
                ),
                headline='Make that bug go away',
                body='We did it.\n\nrefs: ABC-123,  #33 \n',
                bump=VersionBump.PATCH,
                refs={'ABC-123', '#33'},
            ),
        ),
    ]:
        got = CommitInfo.parse(message)
        assert got == want
