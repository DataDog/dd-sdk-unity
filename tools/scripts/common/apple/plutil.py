"""
Utility code for examining iOS app bundles with plutil.
"""
from typing import List

from ..shell import capture_output


def get_bundle_identifier(plist_path: str) -> str:
    # Run plutil to extract the CFBundleIdentifier value from Info.plist: plutil is
    # distributed with macOS so it should be present in any supported iOS build
    # environment
    output, _ = capture_output('plutil', '-extract', 'CFBundleIdentifier', 'raw', plist_path)
    result = output.splitlines()[0]
    if not result:
        raise RuntimeError(f'Failed to parse CFBundleIdentifier from plutil output: {output}')
    return result
