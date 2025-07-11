"""
Utility code for invoking avdmanager, which is used to list and create the AVDs
(Android Virtual Devices) which are run in the Android emulator.
"""
import re
from dataclasses import dataclass
from typing import Optional, List

from common.log import get_default_logger
from common.shell import capture_output

from .util import resolve_android_binary


@dataclass
class Avd:
    name: str
    device: Optional[str]
    path: str
    api_level: int
    tag: str
    abi: str
    skin: Optional[str]
    sdcard: Optional[str]


class AvdManager(object):
    """
    Wrapper for $ANDROID_HOME/cmdline-tools/latest/avdmanager.
    """
    path: str

    def __init__(self, path: str):
        self.path = path

    def list_avds(self) -> List[Avd]:
        stdout, _ = capture_output(self.path, 'list', 'avd')
        return _parse_list_avd_output(stdout)
    
    def create_avd(self, name: str, system_package_path: str, device: str):
        capture_output(
            self.path, 'create', 'avd',
            '-n', name,
            '--package', system_package_path,
            '--device', device,
            '--force',
        )

    @classmethod
    def require(cls) -> 'AvdManager':
        path, error_message = path, error_message = resolve_android_binary('cmdline-tools', 'latest', 'bin', 'avdmanager')
        if error_message:
            raise RuntimeError(f'Failed to find avdmanager: {error_message}')
        return cls(path)


def _parse_list_avd_output(output: str) -> List[Avd]:
    log = get_default_logger()

    # Verify that we have a valid header, then skip it
    lines = output.splitlines()
    header = 'Available Android Virtual Devices:'
    if not lines or not lines[0].startswith(header):
        raise RuntimeError(f'Unexpected avdmanager output: "{header}" did not appear as first line')
    lines = lines[1:]

    # Collect groups of lines delimited by '---------'
    groups: List[List[str]] = []
    current: List[str] = []
    for line in lines:
        if line.count('-') == len(line.strip()):
            if current:
                groups.append(current)
                current = []
        else:
            current.append(line)
    if current:
        groups.append(current)

    # Parse the device details from each group of lines
    avds: List[Avd] = []
    for avd_lines in groups:
        # Read line-by-line to accumulate the details of the current device
        name: Optional[str] = None
        device: Optional[str] = None
        path: Optional[str] = None
        android_version_str: Optional[str] = None
        api_level: Optional[int] = None
        tag: Optional[str] = None
        abi: Optional[str] = None
        skin: Optional[str] = None
        sdcard: Optional[str] = None
        for line in avd_lines:
            name_match = re.search(r'Name: ([^\s]+)', line)
            if name_match:
                name = name_match.group(1)
                continue
            device_match = re.search(r'Device: ([^\s]+)\s*', line)
            if device_match:
                device = device_match.group(1)
                continue
            path_match = re.search(r'Path: (.*)', line)
            if path_match:
                path = path_match.group(1).strip()
                continue
            basedon_match = re.search(r'Based on: Android (.*) Tag\/ABI: ([^\s\/]+)\/([^\s\/]+)', line)
            if basedon_match:
                android_version_str = basedon_match.group(1)
                assert android_version_str
                api_level = _resolve_api_level(android_version_str)
                tag = basedon_match.group(2)
                abi = basedon_match.group(3)
                continue
            skin_match = re.search(r'Skin: (.*)', line)
            if skin_match:
                skin = skin_match.group(1).strip()
                continue
            sdcard_match = re.search(r'Sdcard: (.*)', line)
            if sdcard_match:
                sdcard = sdcard_match.group(1).strip()
                continue
        
        # Validate required attributes, printing a warning and skipping the device if unable to parse
        if not name:
            log.warning('Failed to parse AVD name from avdmanager output')
            continue
        if not path:
            log.warning(f"Failed to parse path from avdmanager output for AVD '{name}'")
            continue
        if not api_level:
            suffix = ''
            if android_version_str:
                suffix = f' (based on Android {android_version_str})'
            log.warning(f"Failed to parse API level from avdmanager output for AVD '{name}'{suffix}")
            continue
        if not tag:
            log.warning(f"Failed to parse tag from avdmanager output for AVD '{name}'")
            continue
        if not abi:
            log.warning(f"Failed to parse ABI from avdmanager output for AVD '{name}'")
            continue

        # We've parsed everything we need to construct a valid Avd
        avds.append(Avd(
            name=name,
            device=device,
            path=path,
            api_level=api_level,
            tag=tag,
            abi=abi,
            skin=skin,
            sdcard=sdcard,
        ))

    return avds


def _resolve_api_level(android_version_str: str) -> Optional[int]:
    # Newer images show API level directly; e.g. 'Android API 36'
    if android_version_str.startswith('API '):
        try:
            return int(android_version_str.split()[1])
        except (IndexError, ValueError):
            return None

    # Older images name the release version; e.g. 'Android 6.0', 'Android 12L'
    ver = android_version_str.split()[0]
    try:
        return {
            '4.0.1': 14,
            '4.0.2': 14,
            '4.0.3': 15,
            '4.0.4': 15,
            '4.1': 16,
            '4.2': 17,
            '4.3': 18,
            '4.4': 19,
            '4.4W': 20,
            '5.0': 21,
            '5.1': 22,
            '6.0': 23,
            '7.0': 24,
            '7.1': 25,
            '8.0': 26,
            '8.1': 27,
            '9.0': 28,
            '10.0': 29,
            '11.0': 30,
            '12.0': 31,
            '12L': 32,
            '13.0': 33,
            '14.0': 34,
            '15.0': 35,
            '16.0': 36,
        }[ver]
    except IndexError:
        return None
