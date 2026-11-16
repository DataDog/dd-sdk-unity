# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Pin format shared by SDKs distributed as release archives."""

from pydantic import BaseModel, ConfigDict, Field

from .semver import VERSION_PATTERN


class ArtifactPin(BaseModel):
    model_config = ConfigDict(extra="forbid")

    name: str = Field(pattern=r"^[^/\\]+\.zip$")
    sha256: str | None = Field(pattern=r"^[0-9a-f]{64}$")
    destination: str = Field(min_length=1)
    strip_prefix: str
    files: dict[str, str] = Field(min_length=1)


class NativeArtifact(ArtifactPin):
    platform: str
    repository: str
    version: str


class SdkPin(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    version: str = Field(pattern=rf"^{VERSION_PATTERN}$")
    artifacts: dict[str, ArtifactPin] = Field(min_length=1)
