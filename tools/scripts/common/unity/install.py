import os
import re
import sys
import subprocess
from dataclasses import dataclass
from enum import Enum
from typing import List, Optional, TextIO

from common.shell import run_cmd


@dataclass
class UnityVersion:
    major: int
    minor: int
    patch: int
    revision: str

    def __str__(self):
        return '%d.%d.%d%s' % (self.major, self.minor, self.patch, self.revision)

    def __eq__(self, other: 'UnityVersion') -> bool:
        if isinstance(other, str):
            return str(self) == other
        if not isinstance(other, UnityVersion):
            return NotImplemented
        lhs = (self.major, self.minor, self.patch, self.revision)
        rhs = (other.major, other.minor, other.patch, other.revision)
        return lhs == rhs

    def __lt__(self, other: 'UnityVersion') -> bool:
        lhs = (self.major, self.minor, self.patch, self._revision_sort_key)
        rhs = (other.major, other.minor, other.patch, other._revision_sort_key)
        return lhs < rhs

    @property
    def is_released(self) -> bool:
        """
        Returns whether this version of Unity is tagged 'f1', 'p2', etc., indicating
        that it's officially released (as opposed to 'a', 'b', 'rc' for alpha/beta/RC).
        """
        return self.revision.startswith('f') or self.revision.startswith('p')
    
    @classmethod
    def parse(cls, s: str) -> 'UnityVersion':
        pattern = re.compile(r'^(\d+)\.(\d+)\.(\d+)((?:a|b|rc|f|p)\d+)$')
        match = pattern.match(s)
        if not match:
            raise ValueError(f'Unexpected format for Unity version: {s}')
        return cls(
            major=int(match.group(1)),
            minor=int(match.group(2)),
            patch=int(match.group(3)),
            revision=match.group(4),
        )
    
    @property
    def _revision_sort_key(self) -> int:
        match = re.match(r'^(a|b|rc|f|p)(\d+)$', self.revision)
        if not match:
            raise ValueError(f'Invalid Unity version revision: {self.revision}')
        rank = {
            'a': 0x20000000,
            'b': 0x40000000,
            'rc': 0x60000000,
            'f': 0x80000000,
            'p': 0xa0000000,
        }[match.group(1)]
        return rank | int(match.group(2))


class UnityLicenseStatus(Enum):
    UNKNOWN = 0
    INVALID = 1
    VALID = 2


@dataclass
class UnityBatchModeResult:
    exitcode: int
    license_status: UnityLicenseStatus


@dataclass
class UnityInstall:
    """
    Represents a single installation of the Unity Editor that's installed on this
    machine.

    `path` is the exact install path reported by Unity Hub, e.g.:

    - '/Applications/Unity/Hub/Editor/$VERSION/Unity.app'
    - 'C:\\Program Files\\Unity\\Hub\\Editor\\%VERSION%\\Editor\\Unity.exe'
    - '/home/$USER/Unity/Hub/Editor/$VERSION/Editor/Unity'

    Call `editor_path`, `licensing_client_path`, etc. to resolve binary paths in a
    OS-agnostic way.
    """
    version: UnityVersion
    architecture: str
    path: str

    @property
    def editor_path(self) -> str:
        """Returns the path to the Unity editor binary for this installation."""
        if self.path.endswith('.app'):
            return os.path.join(self.path, 'Contents', 'MacOS', 'Unity')
        return self.path
    
    @property
    def licensing_client_path(self) -> str:
        """Returns the path to the Unity Licensing Client binary within this installation."""
        if self.path.endswith('.app'):
            subdir_name = 'MacOS'
            if self.version < UnityVersion(2021, 3, 19, 'f1'):
                subdir_name = 'Resources'
            return os.path.join(self.path, 'Contents', 'Frameworks', 'UnityLicensingClient.app', 'Contents', subdir_name, 'Unity.Licensing.Client')
        else:
            unity_root = os.path.dirname(self.path)
            binary_name = 'Unity.Licensing.Client'
            if re.match(r'^[a-zA-Z]:', self.path):
                binary_name += '.exe'
            return os.path.join(unity_root, 'Data', 'Resources', 'Licensing', 'Client', binary_name)
        
    def run_batchmode(self, project_path: str, *args: str, log_path='-', echo_log=True) -> UnityBatchModeResult:
        # If the caller wants Unity log output written to a file, open it
        log_file: Optional[TextIO] = None
        if log_path != '-':
            log_file = open(os.path.abspath(log_path), 'w')

        # Defer a conditional log_file.close()
        try:
            # Prepare an line-handler callback to parse Unity license status from subprocess stdout
            license_status = UnityLicenseStatus.UNKNOWN
            def _read(line: str, is_stderr: bool):
                # Whichever line we've most recently seen determines our status
                nonlocal license_status
                if line.startswith('[Licensing::Client] Successfully resolved entitlement details'):
                    license_status = UnityLicenseStatus.VALID
                elif line.startswith('No valid Unity Editor license found. Please activate your license.'):
                    license_status = UnityLicenseStatus.INVALID

                # If the caller wants us to echo, write each line to Python stdout
                if echo_log:
                    stream_label = '2' if is_stderr else '1'
                    print(f'[{stream_label}] {line}')

                # If we're piping Unity's output to a log file, write there as well
                if log_file:
                    log_file.write(line + '\n')

            # Run Unity, diverting log output to stdout so we can parse it
            unity_args = [self.editor_path, '-batchmode', '-projectPath', project_path, '-logFile', '-', *args]
            exitcode = run_cmd(*unity_args, output_handler=_read)
            return UnityBatchModeResult(
                exitcode=exitcode,
                license_status=license_status,
            )
        finally:
            # If we've been writing Unity output to stdout, flush it
            sys.stdout.flush()

            # Close our log file when finished, if we opened one
            if log_file:
                log_file.close()

    @classmethod
    def parse(cls, line: str) -> Optional['UnityInstall']:
        """Parses a line of output from Unity Hub's 'editors --installed' command."""
        pattern = re.compile(r'^(\S+)\s+\(([^)]+)\), installed at (.+)$')
        match = pattern.match(line)
        if match:
            path = match.group(3)
            if re.match(r'^[a-zA-Z]:\\', path):
                path = path.replace('\\', os.sep)
            return UnityInstall(
                version=UnityVersion.parse(match.group(1)),
                architecture=match.group(2),
                path=path,
            )


def resolve_unity_install(installs: List[UnityInstall], version_prefix: str) -> Optional[UnityInstall]:
    """
    Given a list of available Unity installations, returns the newest one that matches
    the target version constraint, or None if no matching version is available.
    """
    versions = [x.version for x in installs]
    matching_version = match_unity_version(versions, version_prefix)
    if not matching_version:
        return None
    return next((x for x in installs if x.version == matching_version), None)


def match_unity_version(versions: List[UnityVersion], version_prefix: str) -> Optional[UnityVersion]:
    """
    Given a list of available Unity versions and a target version string, returns the
    newest version that satisfies that constraint. '2023.3.55f1' requires an exact
    match; '2023.3.55' will match any revision of that version (including prerelease),
    '2023.3' will match any patch release of 2023.3.x, etc.
    """
    pattern = re.compile(r'^(\d+)(?:\.(\d+)(?:\.(\d+)(?:((?:a|b|rc|f|p)\d+))?)?)?$')
    match = pattern.match(version_prefix)
    if not match:
        raise ValueError(f'Invalid Unity version specifier: {version_prefix}')
    required_major = int(match.group(1))
    required_minor: Optional[int] = int(match.group(2)) if match.group(2) else None
    required_patch: Optional[int] = int(match.group(3)) if match.group(3) else None
    required_revision: Optional[str] = match.group(4) or None

    candidates = [x for x in versions if x.major == required_major]
    if required_minor:
        candidates = [x for x in candidates if x.minor == required_minor]
        if required_patch:
            candidates = [x for x in candidates if x.patch == required_patch]
            if required_revision:
                candidates = [x for x in candidates if x.revision == required_revision]

    if not candidates:
        return None

    newest_candidate = list(sorted(candidates))[-1]
    return newest_candidate
