# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""In-memory release inputs."""

from typing import Annotated

from pydantic import BaseModel, ConfigDict, Field

VERSION_PATTERN = r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"
VersionString = Annotated[str, Field(pattern=rf"^{VERSION_PATTERN}$")]
CommitSha = Annotated[str, Field(pattern=r"^[0-9a-f]{40}$")]
Digest = Annotated[str, Field(pattern=r"^[0-9a-f]{64}$")]


class ReleaseRecord(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)


class Change(ReleaseRecord):
    """One logical change, deduplicated by PR number or direct commit."""

    identifier: str
    title: str
    body: str = ""
    kind: str
    breaking: bool = False  # Release-note input; does not select a major version.
    bookkeeping: bool = False


class CompletedRelease(ReleaseRecord):
    """Source/package tags belonging to an existing GitHub release."""

    version: VersionString
    source_sha: CommitSha
    package_sha: CommitSha


class ReleasePlan(ReleaseRecord):
    """Version-selection result used to build the preparation PR; never persisted."""

    source_sha: CommitSha
    declared_version: VersionString
    base_version: VersionString
    base_sha: CommitSha
    base_override: VersionString | None = None
    version_override: VersionString | None = None
    inferred_version: VersionString
    version: VersionString
    changes: tuple[Change, ...]


class IosPin(ReleaseRecord):
    version: VersionString
    sha256: Digest
    modules: tuple[Annotated[str, Field(pattern=r"^[A-Za-z][A-Za-z0-9]*$")], ...] = Field(
        min_length=1
    )


class AndroidPin(ReleaseRecord):
    version: VersionString


class Preparation(ReleaseRecord):
    """In-memory inputs used to create the preparation PR; never persisted."""

    release: ReleasePlan
    ios: IosPin
    android: AndroidPin
    legacy_notes: str = ""
