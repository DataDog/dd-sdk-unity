"""Tests for release-generated Flags identity.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import json
from pathlib import Path

import pytest

from generate_flags_metadata import generate_metadata, render_metadata


def test_checked_in_metadata_matches_package():
    generate_metadata(Path(__file__).resolve().parents[2] / "packages/Datadog.Unity", check=True)


@pytest.mark.parametrize("version", ["2.0.0", "3.2.1", "3.2.1-rc.2+build.42"])
def test_package_version_drives_shipped_metadata(tmp_path, version):
    (tmp_path / "package.json").write_text(json.dumps({"version": version}))
    with pytest.raises(ValueError, match="missing or stale"):
        generate_metadata(tmp_path, check=True)
    target = generate_metadata(tmp_path)
    assert f'Version = "{version}"' in target.read_text()
    generate_metadata(tmp_path, check=True)
    (tmp_path / "package.json").write_text(json.dumps({"version": "4.0.0"}))
    with pytest.raises(ValueError, match="missing or stale"):
        generate_metadata(tmp_path, check=True)
    generate_metadata(tmp_path)
    generate_metadata(tmp_path, check=True)


@pytest.mark.parametrize("version", ["", "v1.0.0", "1.0", "01.0.0", "1.0.0-01", "1.0.0-a..b", "1.0.0\n", '1.0.0";', None])
def test_rejects_invalid_version(version):
    with pytest.raises(ValueError):
        render_metadata(version)
