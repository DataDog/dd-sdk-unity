# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""In-memory release inputs."""

from typing import Annotated

from pydantic import BaseModel, ConfigDict, Field

from dd_sdk_unity.scripts.common.versions.sdk_pin import SdkPin
from dd_sdk_unity.scripts.common.versions.semver import VERSION_PATTERN

VersionString = Annotated[str, Field(pattern=rf"^{VERSION_PATTERN}$")]
CommitSha = Annotated[str, Field(pattern=r"^[0-9a-f]{40}$")]


class ReleaseRecord(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)


class Change(ReleaseRecord):
    """One merged PR in the release's Git history."""

    number: int
    title: str
    body: str = ""
    kind: str


class ReleasePlan(ReleaseRecord):
    """Version-selection result used to build the preparation PR; never persisted."""

    source_sha: CommitSha
    base_version: VersionString
    base_sha: CommitSha
    version: VersionString
    changes: tuple[Change, ...]


class AndroidPin(ReleaseRecord):
    version: VersionString


class Preparation(ReleaseRecord):
    """In-memory inputs used to create the preparation PR; never persisted."""

    release: ReleasePlan
    ios: SdkPin
    android: AndroidPin
    cpp: SdkPin
