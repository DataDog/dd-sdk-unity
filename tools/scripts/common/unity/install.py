"""
Utility code for resolving specific versions of the Unity editor, and for invoking the
Unity editor binary in order to run headless commands.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import re
import threading
import subprocess
import time
from dataclasses import dataclass
from typing import List, Optional

from .diagnostics import UnityDiagnostics


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

    @classmethod
    def parse(cls, s: str) -> 'UnityVersion':
        pattern = re.compile(r'^(\d+)\.(\d+)\.(\d+)((?:a|b|rc|f|p)\d+)$')
        match = pattern.match(s)
        if not match:
            #raise ValueError(f'Unexpected format for Unity version: {s}')
            return cls(0, 0, 0, 'Invalid')
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


@dataclass
class UnityInstall:
    """
    Represents a single installation of the Unity Editor that's available on this
    machine.

    `path` is the exact install path reported by Unity CLI, e.g.:

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
    
    def run_batchmode(self, project_path: str, *args: str, log_path: str, diagnostics: bool = False, timeout_seconds: Optional[float] = None) -> int:
        # Create the log file before the tail thread opens it.
        if not os.path.isfile(log_path):
            os.makedirs(os.path.dirname(log_path), exist_ok=True)
            with open(log_path, 'w'):
                pass

        quiet_warnings = os.environ.get('CI', '').lower() == 'true'
        warning_pattern = re.compile(r'(?:^|:\s*)warning\s+[A-Z]+\d+:', re.IGNORECASE)
        warning_count = 0
        lightmapper_count = 0
        last_output = (time.monotonic(), 'No Editor output yet')

        def _read(line: str):
            nonlocal warning_count, lightmapper_count, last_output
            if quiet_warnings and line.strip() == 'Falling back to CPU lightmapper.':
                lightmapper_count += 1
                if lightmapper_count > 1:
                    return
            if quiet_warnings and warning_pattern.search(line):
                warning_count += 1
                return
            last_output = (time.monotonic(), line)
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
            tail_thread = threading.Thread(target=_tail_log, daemon=diagnostics)
            tail_thread.start()
            ready_event.wait()

            # Run Unity in batchmode with our desired args, logging to the specified file
            unity_args = [self.editor_path, '-batchmode', '-projectPath', project_path, '-logFile', log_path, *args]
            if diagnostics:
                watchdog = UnityDiagnostics(log_path)
                exitcode = watchdog.run(unity_args, lambda: (*last_output, tail_thread.is_alive()), timeout_seconds=timeout_seconds)
            elif timeout_seconds is not None:
                from common.shell import stop_process
                process = subprocess.Popen(unity_args, start_new_session=True)
                try:
                    exitcode = process.wait(timeout=timeout_seconds)
                finally:
                    stop_process(process)
            else:
                exitcode = subprocess.call(unity_args)

            # Let the tail thread finish reading from the log file
            stop_event.set()
            tail_thread.join(timeout=30 if diagnostics else None)
            if diagnostics and tail_thread.is_alive():
                watchdog.capture(watchdog.process, 'Editor exited, but the log reader did not finish within 30 seconds')
            if warning_count:
                print(f'Suppressed {warning_count} compiler/analyzer warnings from the CI console. Full log: {log_path}')
            if lightmapper_count > 1:
                print(f'Suppressed {lightmapper_count - 1} repeated CPU lightmapper fallback messages from the CI console. Full log: {log_path}')

            return exitcode
        except:
            # Stop the tail thread if we throw an error, get a SIGINT, etc
            stop_event.set()
            tail_thread.join(timeout=5)
            raise


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
    if required_minor is not None:
        candidates = [x for x in candidates if x.minor == required_minor]
        if required_patch is not None:
            candidates = [x for x in candidates if x.patch == required_patch]
            if required_revision:
                candidates = [x for x in candidates if x.revision == required_revision]

    if not candidates:
        return None

    newest_candidate = list(sorted(candidates))[-1]
    return newest_candidate
