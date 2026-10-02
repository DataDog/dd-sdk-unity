"""Generate Flags request identity from the Unity package release version.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import argparse
import json
from pathlib import Path
import re


def render_metadata(version: str) -> str:
    number = r"(?:0|[1-9][0-9]*)"
    prerelease = rf"(?:{number}|[0-9]*[A-Za-z-][0-9A-Za-z-]*)"
    semver = rf"{number}\.{number}\.{number}(?:-{prerelease}(?:\.{prerelease})*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?"
    if not isinstance(version, str) or not re.fullmatch(semver, version):
        raise ValueError("The Unity package must have a valid release version.")
    return f'''// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.
// Generated from package.json by tools/scripts/generate_flags_metadata.py. Do not edit.

namespace Datadog.Unity.Flags
{{
    internal static class FlagsSdkMetadata
    {{
        internal const string Name = "dd-sdk-unity";
        internal const string Version = "{version}";
    }}
}}
'''


def generate_metadata(package_root: Path, check: bool = False) -> Path:
    version = json.loads((package_root / "package.json").read_text())["version"]
    target = package_root / "Runtime/Flags/FlagsSdkMetadata.cs"
    expected = render_metadata(version)
    if check:
        if not target.is_file() or target.read_text() != expected:
            raise ValueError("Flags SDK metadata is missing or stale. Run ./run-script generate_flags_metadata.")
    else:
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(expected)
    return target


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    generate_metadata(Path(__file__).resolve().parents[2] / "packages/Datadog.Unity", args.check)
