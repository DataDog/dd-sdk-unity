"""Tests for release-generated Flags identity."""
import json
from pathlib import Path

import pytest

from generate_flags_metadata import generate_metadata, render_metadata


def test_checked_in_metadata_matches_package():
    generate_metadata(Path(__file__).resolve().parents[2] / "packages/Datadog.Unity", check=True)


def test_prerelease_version_is_preserved(tmp_path):
    (tmp_path / "Runtime/Flags").mkdir(parents=True)
    version = "3.2.1-rc.2+build.42"
    (tmp_path / "package.json").write_text(json.dumps({"version": version}))
    target = generate_metadata(tmp_path)
    assert f'Version = "{version}"' in target.read_text()
    generate_metadata(tmp_path, check=True)
    (tmp_path / "package.json").write_text(json.dumps({"version": "3.2.2"}))
    with pytest.raises(ValueError, match="stale"):
        generate_metadata(tmp_path, check=True)


@pytest.mark.parametrize("version", ["", "v1.0.0", "1.0", "1.0.0\n", '1.0.0";', None])
def test_rejects_invalid_version(version):
    with pytest.raises(ValueError):
        render_metadata(version)
