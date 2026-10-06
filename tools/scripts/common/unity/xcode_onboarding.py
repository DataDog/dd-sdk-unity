"""
Bounded, opt-in Xcode onboarding experiment for the CI VM.

Unless explicitly stated otherwise, all files in this repository are licensed under
the Apache License Version 2.0. Copyright 2026-Present Datadog, Inc.
"""
import json
from pathlib import Path
import plistlib
import re
import subprocess
import tempfile
import time

POLL_SECONDS = 30
COMMAND_TIMEOUT = 10
ATTEMPT_SECONDS = 90


def run_capture(command, output, timeout=COMMAND_TIMEOUT):
    """Use a file, so inherited pipes cannot prevent timeout cleanup."""
    with output.open('w') as log:
        try:
            process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT,
                                       stdin=subprocess.DEVNULL, start_new_session=True)
        except OSError as error:
            log.write(f'Probe unavailable: {error}\n')
            return None
        try:
            return process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            log.write(f'\nProbe timed out after {timeout}s\n')
            return None
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=2)


def redact_preferences(value):
    if isinstance(value, dict):
        return {
            key: '[redacted]' if re.search(r'password|token|secret|credential|authorization|private.?key|api.?key',
                                          key, re.IGNORECASE) else redact_preferences(item)
            for key, item in value.items()
        }
    if isinstance(value, list):
        return [redact_preferences(item) for item in value]
    return value


class XcodeOnboardingProbe:
    def __init__(self, directory, message, record=None):
        self.directory = Path(directory)
        self.message = message
        self.record = record or (lambda label: None)
        self.done = False
        self.pid = None
        self.phase = 'probe'
        self.next_poll = 0
        self.read_count = 0
        self.verify_count = 0
        self.before = None
        self.preferences_saved = False
        path = self.directory / 'xcode-selected-path.log'
        if run_capture(['/usr/bin/xcode-select', '-p'], path, timeout=3) != 0:
            raise RuntimeError(f'Cannot resolve selected Xcode; see {path}')
        developer = path.read_text().strip()
        if not developer.endswith('/Contents/Developer'):
            raise RuntimeError('Selected developer directory is not an Xcode application')
        self.executable = developer[:-len('/Contents/Developer')] + '/Contents/MacOS/Xcode'
        self.preexisting = self._pids('xcode-processes-before.log')
        self.message(f'Xcode UI probe enabled for newly started {self.executable}')

    def _pids(self, filename):
        path = self.directory / filename
        if run_capture(['/bin/ps', '-axo', 'pid=,comm='], path, timeout=3) != 0:
            raise RuntimeError(f'Cannot inspect Xcode process names; see {path}')
        pids = set()
        for line in path.read_text().splitlines():
            fields = line.strip().split(None, 1)
            if len(fields) == 2 and fields[1] == self.executable:
                pids.add(int(fields[0]))
        return pids

    def _ui(self, mode, filename):
        path = self.directory / filename
        script = Path(__file__).with_suffix('.js')
        remaining = max(0.1, min(COMMAND_TIMEOUT, self.deadline - time.monotonic()))
        result = run_capture(['/usr/bin/osascript', '-l', 'JavaScript', str(script), str(self.pid), mode],
                             path, timeout=remaining)
        if result != 0:
            raise RuntimeError(f'UI {mode} unavailable (exit {result}); see {path}')
        return json.loads(path.read_text())

    def _preferences(self, label):
        # Raw preferences never become an artifact; redact credential-like keys first.
        with tempfile.TemporaryFile() as output, (self.directory / 'xcode-preference-errors.log').open('a') as errors:
            try:
                result = subprocess.run(['/usr/bin/defaults', 'export', 'com.apple.dt.Xcode', '-'],
                                        stdout=output, stderr=errors, timeout=3)
                output.seek(0)
                data = redact_preferences(plistlib.loads(output.read())) if result.returncode == 0 else {}
            except (OSError, subprocess.TimeoutExpired, plistlib.InvalidFileException, ValueError) as error:
                errors.write(f'Preference snapshot failed: {error}\n')
                data = {}
        (self.directory / f'xcode-preferences-{label}.json').write_text(
            json.dumps(data, indent=2, sort_keys=True, default=str) + '\n')
        return data

    def _finish(self, reason):
        self.done = True
        self.message(f'Xcode UI probe: {reason}')
        self.close()

    def close(self):
        if self.before is not None and not self.preferences_saved:
            self.preferences_saved = True
            after = self._preferences('after')
            changes = {
                key: {'before': self.before.get(key), 'after': after.get(key),
                      'before_present': key in self.before, 'after_present': key in after}
                for key in sorted(self.before.keys() | after.keys())
                if (key in self.before) != (key in after) or self.before.get(key) != after.get(key)
            }
            (self.directory / 'xcode-preferences-diff.json').write_text(
                json.dumps(changes, indent=2, sort_keys=True, default=str) + '\n')
            self.message(f'Xcode preference comparison saved ({len(changes)} changed keys)')

    def poll(self):
        now = time.monotonic()
        if self.done or now < self.next_poll:
            return
        try:
            if self.pid is None:
                candidates = self._pids('xcode-processes-latest.log') - self.preexisting
                if not candidates:
                    self.next_poll = now + POLL_SECONDS
                    return
                if len(candidates) != 1:
                    self._finish('multiple new Xcode instances; see process snapshots')
                    return
                self.pid = candidates.pop()
                self.deadline = now + ATTEMPT_SECONDS
                self.before = self._preferences('before')
                self.message(f'Probing Xcode PID {self.pid}; UI attempt limited to {ATTEMPT_SECONDS}s')
            if now >= self.deadline:
                self._finish('attempt deadline exceeded')
                return
            if self.phase == 'probe':
                self.read_count += 1
                state = self._ui('probe', f'xcode-ui-probe-{self.read_count:02d}.json')
                if not state.get('chooser'):
                    if self.read_count >= 3:
                        self._finish('component chooser not recognized; inspect UI snapshots')
                    else:
                        self.next_poll = now + 5
                    return
                self.record('xcode-onboarding')
                result = self._ui('deselect', 'xcode-ui-deselect.json')
                if result.get('outcome') != 'deselected':
                    self._finish(f'no continuation: {result.get("outcome")}')
                    return
                self.phase = 'continue'
            elif self.phase == 'continue':
                result = self._ui('continue', 'xcode-ui-continue.json')
                if result.get('outcome') != 'submitted':
                    self._finish(f'no continuation: {result.get("outcome")}')
                    return
                self.phase = 'verify'
            else:
                self.verify_count += 1
                state = self._ui('probe', f'xcode-ui-verify-{self.verify_count:02d}.json')
                if not state.get('chooser'):
                    self._finish('component chooser disappeared; inspect final UI snapshot for remaining windows')
                    return
                if self.verify_count >= 3:
                    self._finish('component chooser remained after continuation')
                    return
            self.next_poll = time.monotonic() + 5
        except (OSError, RuntimeError, ValueError) as error:
            self._finish(str(error))
