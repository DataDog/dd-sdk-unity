"""
Utility code for resolving specific versions of the Unity editor, and for invoking the
Unity editor binary in order to run headless commands.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import re
import tempfile
import threading
import subprocess
import time
from dataclasses import dataclass
from enum import Enum
from typing import List, Optional


@dataclass
class UnityVersion:
    major: int
    minor: int
    patch: int
    revision: str

    def __str__(self):
        return '%d.%d.%d%s' % (self.major, self.minor, self.patch, self.revision)

    def __eq__(self, other: object) -> bool:
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
    Represents a single installation of the Unity Editor that's available on this
    machine.

    `path` is the exact install path reported by Unity Hub, e.g.:

    - '/Applications/Unity/Hub/Editor/$VERSION/Unity.app'
    - 'C:\\Program Files\\Unity\\Hub\\Editor\\%VERSION%\\Editor\\Unity.exe'
    - '/home/$USER/Unity/Hub/Editor/$VERSION/Editor/Unity'

    Call `editor_path` to resolve binary paths in a OS-agnostic way.
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
        # If the caller doesn't care to have the log file saved anywhere, write it to a
        # temp file that we can tail
        log_file_is_temporary = False
        if log_path == '-':
            with tempfile.NamedTemporaryFile(suffix='.log', delete=False) as tmp:
                log_path = tmp.name
            log_file_is_temporary = True
        else:
            # If we have a caller-specified log file path, make sure it exists so we
            # can open it for read before we've launched Unity
            if not os.path.isfile(log_path):
                os.makedirs(os.path.dirname(log_path), exist_ok=True)
                with open(log_path, 'w') as fp:
                    pass

        # Prepare an line-handler callback to parse Unity license status from stdout
        license_status = UnityLicenseStatus.UNKNOWN
        def _read(line: str):
            # Whichever line we've most recently seen determines our status
            nonlocal license_status
            if line.startswith('[Licensing::Client] Successfully resolved entitlement details'):
                license_status = UnityLicenseStatus.VALID
            elif line.startswith('No valid Unity Editor license found. Please activate your license.'):
                license_status = UnityLicenseStatus.INVALID

            # If the caller wants us to echo, write each line to Python stdout
            if echo_log:
                max_retries = 10
                delay = 0.01
                for attempt in range(max_retries):
                    try:
                        print(line)
                        break
                    except BlockingIOError:
                        if attempt >= max_retries:
                            raise
                        time.sleep(delay)
                        delay *= 2

        # Prepare a separate thread to tail output from the Unity log file, passing
        # each line to the _read callback - trying to pipe output via subprocess.Popen
        # can get us into trouble with mysterious buffering issues that will cause
        # Unity to hang
        stop_event = threading.Event()
        ready_event = threading.Event()

        def _tail_log():
            with open(log_path, 'r') as fp:
                # Seek to EOF
                fp.seek(0, 2)
                ready_event.set()

                # Continually poll for new lines in the file
                while True:
                    line = fp.readline()
                    if line:
                        _read(line.strip('\n'))
                    elif stop_event.is_set():
                        break
                    else:
                        time.sleep(0.01)

                # Process has exited; read all remaining lines and close the file
                while True:
                    line = fp.readline()
                    if not line:
                        break
                    _read(line.rstrip('\n'))

        try:
            tail_thread = threading.Thread(target=_tail_log)
            tail_thread.start()
            ready_event.wait()

            # Run Unity in batchmode with our desired args, logging to the specified file
            unity_args = [self.editor_path, '-batchmode', '-projectPath', project_path, '-logFile', log_path, *args]
            exitcode = subprocess.call(unity_args)

            # Let the tail thread finish reading from the log file
            stop_event.set()
            tail_thread.join()

            return UnityBatchModeResult(
                exitcode=exitcode,
                license_status=license_status,
            )
        except:
            # Stop the tail thread if we throw an error, get a SIGINT, etc
            stop_event.set()
            raise
        finally:
            if log_file_is_temporary:
                os.remove(log_path)

    @classmethod
    def parse(cls, line: str) -> Optional['UnityInstall']:
        """Parses a line of output from Unity Hub's 'editors --installed' command."""
        pattern = re.compile(r'^(\S+)\s+\(([^)]+)\),? installed at (.+)$')
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
        return None


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
