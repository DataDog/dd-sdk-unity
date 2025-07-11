"""
Utility code for modifying package.json, which specifies the version number used for
the Datadog.Unity package in Unity Package Manager (UPM).
"""
import json
from typing import IO

from .semver import Version


def modify_package_json(path: str, new_version: Version):
    with open(path) as fp:
        new_text = _modify_package_json_impl(fp, new_version)
    with open(path, 'w') as fp:
        fp.write(new_text)


def _modify_package_json_impl(infile: IO[str], new_version: Version) -> str:
    json_obj = json.load(infile)
    json_obj['version'] = str(new_version)
    return json.dumps(json_obj, indent=2) + '\n'
