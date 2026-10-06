"""
Diagnostics for CI integration-test Editor launches.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import faulthandler
import os
from pathlib import Path
import shlex
import signal
import subprocess
import sys
import time

from .xcode_onboarding import XcodeOnboardingProbe

HEARTBEAT_SECONDS = 60
STALL_SECONDS = 5 * 60
UNITY_TIMEOUT_SECONDS = 15 * 60
POLL_SECONDS = 5
VIDEO_SECONDS = 60
VIDEO_GRACE_SECONDS = 15


class UnityDiagnostics:
    def __init__(self, log_path):
        self.directory = Path(log_path).with_suffix('').with_name(Path(log_path).stem + '-diagnostics')
        self.directory.mkdir(parents=True, exist_ok=True)
        self.log_path = Path(log_path)
        self.snapshot_count = 0
        self.video_count = 0
        self.recorder = None
        self.video_disabled = (sys.platform != 'darwin'
                               or os.environ.get('UNITY_CI_SCREEN_CAPTURE', '1') == '0')

    def message(self, text):
        text = f'{time.strftime("%Y-%m-%d %H:%M:%S UTC", time.gmtime())} {text}'
        with (self.directory / 'watchdog.log').open('a') as log:
            log.write(text + '\n')
        try:
            print(text, file=sys.stderr, flush=True)
        except OSError:
            pass  # Keep collecting file diagnostics if the runner closes its console.

    def tool(self, command, output, timeout=15):
        with output.open('w') as log:
            try:
                result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
                if result.returncode:
                    log.write(f'\nDiagnostic command exited with {result.returncode}\n')
            except (OSError, subprocess.TimeoutExpired) as error:
                log.write(f'\nDiagnostic command failed: {error}\n')

    def capture(self, process, reason):
        self.snapshot_count += 1
        prefix = f'{self.snapshot_count:02d}'
        self.message(f'Capturing diagnostics: {reason}; directory: {self.directory}')
        with (self.directory / f'{prefix}-python-threads.log').open('w') as log:
            faulthandler.dump_traceback(file=log, all_threads=True)

        # Executable names only: do not dump command-line credentials or environments.
        snapshot = self.directory / f'{prefix}-processes.log'
        self.tool(['/bin/ps', '-axo', 'pid=,ppid=,%cpu=,rss=,etime=,stat=,comm='], snapshot)
        if sys.platform == 'darwin' and process.poll() is None:
            records = []
            for line in snapshot.read_text().splitlines():
                fields = line.split(None, 6)
                if len(fields) == 7:
                    try:
                        records.append((int(fields[0]), int(fields[1]), float(fields[2]), fields[6]))
                    except ValueError:
                        pass
            descendants = {process.pid}
            while True:
                expanded = descendants | {pid for pid, parent, _, _ in records if parent in descendants}
                if expanded == descendants:
                    break
                descendants = expanded
            candidates = [
                row for row in records
                if row[0] in descendants or Path(row[3]).name in ('Xcode', 'Simulator')
            ]
            candidates.sort(key=lambda row: -row[2])
            pids = [process.pid] + [row[0] for row in candidates if row[0] != process.pid][:3]
            for pid in pids:
                self.tool(['/usr/bin/sample', str(pid), '5', '10', '-mayDie',
                           '-file', str(self.directory / f'{prefix}-process-{pid}-sample.log')],
                          self.directory / f'{prefix}-sample-{pid}-command.log')
        if process.poll() is None:
            self.start_video(f'stall-{prefix}')

    def start_video(self, label):
        self.check_video()
        if self.video_disabled or self.recorder is not None or self.video_count >= 3:
            return
        self.video_count += 1
        self.video_path = self.directory / f'desktop-{self.video_count:02d}-{label}.mov'
        self.video_started = time.monotonic()
        with (self.directory / 'screen-capture.log').open('a') as log:
            try:
                self.recorder = subprocess.Popen(
                    ['/usr/sbin/screencapture', '-x', '-v', '-V', str(VIDEO_SECONDS), '-D1', str(self.video_path)],
                    stdout=log, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                )
                self.message(f'Started bounded desktop capture PID {self.recorder.pid}: {self.video_path}')
            except OSError as error:
                log.write(f'Screen recording unavailable: {error}\n')
                self.video_disabled = True
                self.message(f'Screen recording unavailable; see {self.directory / "screen-capture.log"}')

    def check_video(self):
        if self.recorder is None:
            return
        if self.recorder.poll() is None:
            if time.monotonic() - self.video_started > VIDEO_SECONDS + VIDEO_GRACE_SECONDS:
                self.message('Screen recorder exceeded its limit; stopping capture.')
                self.stop_video()
                self.video_disabled = True
            return
        if self.recorder.returncode or not self.video_path.is_file() or not self.video_path.stat().st_size:
            self.video_disabled = True
            self.message(f'Screen recording unavailable (exit {self.recorder.returncode}); '
                         f'see {self.directory / "screen-capture.log"}')
        self.recorder = None

    def stop_video(self):
        recorder, self.recorder = self.recorder, None
        if recorder is not None and recorder.poll() is None:
            try:
                recorder.send_signal(signal.SIGINT)
                try:
                    recorder.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    recorder.kill()
                    recorder.wait(timeout=5)
            except (OSError, subprocess.TimeoutExpired) as error:
                self.message(f'Could not stop screen recorder: {error}')

    def run(self, command, status, timeout_seconds=None):
        timeout_seconds = UNITY_TIMEOUT_SECONDS if timeout_seconds is None else timeout_seconds
        started = time.monotonic()
        last_capture = started
        next_heartbeat = started + HEARTBEAT_SECONDS
        probe = None
        if (sys.platform == 'darwin' and os.environ.get('CI', '').lower() == 'true'
                and os.environ.get('UNITY_CI_XCODE_UI_PROBE') == '1'):
            try:
                probe = XcodeOnboardingProbe(self.directory, self.message, self.start_video)
            except Exception as error:
                self.message(f'Xcode UI probe unavailable: {error}')
        self.message(f'Launching Unity: {shlex.join(command)}')
        process = subprocess.Popen(command, start_new_session=True)  # Inherit streams; Unity writes to its log file.
        self.process = process
        try:
            self.message(f'Unity PID {process.pid}; timeout {timeout_seconds // 60} minutes')
            self.start_video('startup')
            while True:
                remaining = timeout_seconds - (time.monotonic() - started)
                if remaining <= 0:
                    self.capture(process, 'Unity runtime limit exceeded')
                    raise RuntimeError(f'Unity exceeded {timeout_seconds // 60} minutes; '
                                       f'diagnostics: {self.directory}')
                try:
                    result = process.wait(timeout=min(POLL_SECONDS, remaining))
                    self.message(f'Unity PID {process.pid} exited with {result}')
                    return result
                except subprocess.TimeoutExpired:
                    pass
                now = time.monotonic()
                last_output, line, tail_alive = status()
                if now >= next_heartbeat:
                    size = self.log_path.stat().st_size if self.log_path.exists() else 0
                    self.message(f'Unity PID {process.pid}: elapsed {int(now - started)}s, '
                                 f'no visible output {int(now - last_output)}s, log {size} bytes, '
                                 f'log reader alive={tail_alive}; last output: {line[:200]}')
                    next_heartbeat = now + HEARTBEAT_SECONDS
                self.check_video()
                if probe is not None:
                    try:
                        probe.poll()
                    except Exception as error:
                        probe.done = True
                        self.message(f'Xcode UI probe stopped: {error}')
                if now - last_output >= STALL_SECONDS and now - last_capture >= STALL_SECONDS:
                    self.capture(process, 'no visible Editor progress')
                    last_capture = time.monotonic()
        finally:
            try:
                if probe is not None:
                    try:
                        probe.close()
                    except Exception as error:
                        self.message(f'Xcode preference comparison unavailable: {error}')
            finally:
                self.stop_video()
                if process.poll() is None:
                    self.message(f'Stopping owned Unity PID {process.pid}')
                    if os.name == 'nt':
                        process.terminate()
                    else:
                        os.killpg(process.pid, signal.SIGTERM)
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        if os.name == 'nt':
                            process.kill()
                        else:
                            os.killpg(process.pid, signal.SIGKILL)
                        process.wait(timeout=5)
