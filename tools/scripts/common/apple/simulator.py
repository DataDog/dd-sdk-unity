"""Build exported UTF tests with xcodebuild and run them on an owned iOS Simulator.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import json
from pathlib import Path
import plistlib
import shlex
import shutil
import sys
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET

from common.log import get_default_logger
from common.shell import stop_process

RESULT_FILENAME = 'nunit-integration-test-ios.xml'


def _capture(*command, timeout=30):
    return subprocess.check_output(command, text=True, timeout=timeout).strip()


def _run(command, log_path, deadline):
    log = get_default_logger()
    log.info('Running: ' + shlex.join(command))
    with Path(log_path).open('a') as output:
        output.write('$ ' + shlex.join(command) + '\n')
        output.flush()
        process = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT,
                                   stdin=subprocess.DEVNULL, start_new_session=True)
        try:
            while True:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise RuntimeError(f'iOS native test timeout; see {log_path}')
                try:
                    result = process.wait(timeout=min(60, remaining))
                    break
                except subprocess.TimeoutExpired:
                    log.info(f'Native command PID {process.pid} is still running; log: {log_path}')
            if result:
                tail = '\n'.join(Path(log_path).read_text(errors='replace').splitlines()[-30:])
                raise RuntimeError(f'Command exited with {result}; see {log_path}\n{tail}')
        finally:
            stop_process(process)


def _simulator_type(runtimes, devices):
    candidates = [r for r in runtimes if r.get('isAvailable') and
                  r.get('identifier', '').startswith('com.apple.CoreSimulator.SimRuntime.iOS-')]
    candidates.sort(key=lambda r: tuple(int(part) for part in r['version'].split('.')), reverse=True)
    for runtime in candidates:
        phones = [d for d in devices.get(runtime['identifier'], [])
                  if d.get('isAvailable') and 'iPhone' in d.get('deviceTypeIdentifier', '')]
        if phones:
            return runtime['identifier'], phones[0]['deviceTypeIdentifier']
    raise RuntimeError('No available iOS runtime with an iPhone device type; check simctl list devices available')


def collect_results(source, destination):
    """UTF's runtime callback returns a suite; the existing converter expects test-run."""
    root = ET.parse(source).getroot()
    if root.tag == 'test-suite':
        run = ET.Element('test-run', dict(root.attrib))
        run.append(root)
        root = run
    if root.tag != 'test-run' or not root.findall('.//test-case'):
        raise RuntimeError('The Simulator did not produce a nonempty NUnit test run')
    # Runtime ToXml retains discovery counts even when the run is category-filtered.
    for suite in root.iter():
        if suite.tag in ('test-run', 'test-suite'):
            suite.set('testcasecount', str(len(suite.findall('.//test-case'))))
    ET.ElementTree(root).write(destination, encoding='utf-8', xml_declaration=True)


def _print_player_progress(reader):
    log = get_default_logger()
    for line in reader:
        marker = line.find('[Integration tests]')
        if marker >= 0:
            log.info(line[marker:].rstrip())


def _collect_failure_logs(udid, bundle_id, app_name, container, launch, started, directory):
    directory = directory / udid
    directory.mkdir(parents=True, exist_ok=True)
    log = get_default_logger()
    log.warning(f'Collecting Simulator failure diagnostics before cleanup: {directory}')
    (directory / 'launch.json').write_text(json.dumps({
        'simulator_udid': udid, 'bundle_id': bundle_id, 'executable': app_name,
        'console_launcher_pid': launch.pid, 'console_launcher_exit_code': launch.poll(),
        'launch_started_unix': started, 'collected_unix': time.time(),
    }, indent=2))

    def command(arguments, filename, timeout):
        with (directory / filename).open('w') as output:
            try:
                result = subprocess.run(arguments, stdin=subprocess.DEVNULL, stdout=output, stderr=subprocess.STDOUT, timeout=timeout)
                output.write(f'\nDiagnostic command exit code: {result.returncode}\n')
            except (OSError, subprocess.TimeoutExpired) as error:
                output.write(f'\nDiagnostic command unavailable: {error}\n')

    predicate = (f'process == {json.dumps(app_name)} OR process == "ReportCrash" OR '
                 f'process == "runningboardd" OR eventMessage CONTAINS {json.dumps(bundle_id)}')
    command(['xcrun', 'simctl', 'spawn', udid, 'log', 'show', '--style', 'compact',
             '--info', '--debug', '--start', f'@{int(started) - 5}', '--predicate', predicate],
            'system.log', 20)
    command(['xcrun', 'simctl', 'spawn', udid, 'launchctl', 'list'], 'launch-services.log', 10)

    # ReportCrash may write the .ips file asynchronously after the app exits.
    reports = directory / 'crash-reports'
    reports.mkdir()
    sources = [Path.home() / 'Library/Logs/DiagnosticReports',
               Path.home() / 'Library/Developer/CoreSimulator/Devices' / udid / 'data/Library/Logs/CrashReporter']
    copied = set()
    deadline = time.monotonic() + 10
    while True:
        for index, source in enumerate(sources):
            if not source.is_dir():
                continue
            for report in source.glob(app_name + '*'):
                if (report.is_file() and report.suffix in ('.ips', '.crash', '.hang')
                        and report.name.startswith(app_name) and report.stat().st_mtime >= started
                        and report not in copied):
                    shutil.copy2(report, reports / f'{index}-{report.name}')
                    copied.add(report)
        if copied or time.monotonic() >= deadline:
            break
        time.sleep(0.5)
    (directory / 'crash-reports.log').write_text(
        '\n'.join(str(report) for report in sorted(copied)) or 'No matching new app crash report found.\n')
    sdk_reports = container / 'Library/Caches/CrashReports'
    if sdk_reports.is_dir():
        shutil.copytree(sdk_reports, directory / 'sdk-crash-reports')


def run_simulator_tests(export_path, nunit_path, log_path, timeout=600):
    log = get_default_logger()
    export_path = Path(export_path)
    if not (export_path / 'Unity-iPhone.xcodeproj').is_dir():
        raise RuntimeError(f'Unity did not export an Xcode project to {export_path}')
    deadline = time.monotonic() + timeout
    runtimes = json.loads(_capture('xcrun', 'simctl', 'list', 'runtimes', 'available', '-j'))['runtimes']
    devices = json.loads(_capture('xcrun', 'simctl', 'list', 'devices', 'available', '-j'))['devices']
    runtime, device_type = _simulator_type(runtimes, devices)
    udid = _capture('xcrun', 'simctl', 'create', 'Datadog Integration Tests ' + uuid.uuid4().hex[:8],
                    device_type, runtime)
    log.info(f'Created owned iOS Simulator {udid}, runtime {runtime}')
    launch = None
    bundle_id = None
    app_name = None
    container = None
    launch_started = None
    try:
        derived = export_path.parent / 'ios-derived-data'
        workspace = export_path / 'Unity-iPhone.xcworkspace'
        project_args = ['-workspace', str(workspace)] if workspace.is_dir() else [
            '-project', str(export_path / 'Unity-iPhone.xcodeproj')]
        _run(['xcodebuild', *project_args, '-scheme', 'Unity-iPhone', '-configuration', 'Debug',
              '-sdk', 'iphonesimulator', '-destination', f'platform=iOS Simulator,id={udid}',
              '-derivedDataPath', str(derived), 'CODE_SIGNING_ALLOWED=NO', 'build'], log_path, deadline)
        apps = list((derived / 'Build/Products/Debug-iphonesimulator').glob('*.app'))
        if len(apps) != 1:
            raise RuntimeError(f'Expected one built test app, found {apps}')
        with (apps[0] / 'Info.plist').open('rb') as info:
            app_info = plistlib.load(info)
            bundle_id = app_info['CFBundleIdentifier']
            app_name = app_info['CFBundleExecutable']
        _run(['xcrun', 'simctl', 'boot', udid], log_path, deadline)
        _run(['xcrun', 'simctl', 'bootstatus', udid, '-b'], log_path, deadline)
        _run(['xcrun', 'simctl', 'install', udid, str(apps[0])], log_path, deadline)
        container = Path(_capture('xcrun', 'simctl', 'get_app_container', udid, bundle_id, 'data'))
        result_path = container / 'Documents' / RESULT_FILENAME
        # Never accept XML left by an earlier app execution.
        result_path.unlink(missing_ok=True)
        result_path.with_suffix('.xml.tmp').unlink(missing_ok=True)
        player_log = Path(log_path).with_name(Path(log_path).stem + '-player.log')
        log.info(f'Launching {bundle_id} via simctl; player log: {player_log}')
        with player_log.open('w') as output, player_log.open(errors='replace') as reader:
            launch_started = time.time()
            launch_command = ['xcrun', 'simctl', 'launch', '--console-pty', udid, bundle_id]
            with Path(log_path).open('a') as native_log:
                native_log.write('$ ' + shlex.join(launch_command) + '\n')
            launch = subprocess.Popen(launch_command,
                                      stdout=output, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                                      start_new_session=True)
            next_status = time.monotonic() + 60
            while True:
                _print_player_progress(reader)
                if result_path.is_file():
                    break
                if launch.poll() is not None:
                    # The file is moved into place before the app requests quit.
                    if result_path.is_file():
                        break
                    raise RuntimeError(f'Simulator console launcher exited with {launch.returncode} before writing NUnit XML; see {player_log}')
                if time.monotonic() >= deadline:
                    raise RuntimeError(f'Timed out waiting for NUnit XML at {result_path}; see {player_log}')
                if time.monotonic() >= next_status:
                    log.info(f'Waiting for NUnit results from Simulator {udid}; player log: {player_log}')
                    next_status = time.monotonic() + 60
                time.sleep(0.2)
            _print_player_progress(reader)
            collect_results(result_path, nunit_path)
            log.info(f'Collected Simulator NUnit results: {nunit_path}')
    finally:
        primary_error = sys.exc_info()[0] is not None
        cleanup_errors = []
        if launch is not None:
            if primary_error:
                try:
                    _collect_failure_logs(udid, bundle_id, app_name, container, launch, launch_started,
                                          Path(log_path).with_suffix('').with_name(Path(log_path).stem + '-diagnostics'))
                except Exception as error:
                    log.warning(f'Simulator diagnostic collection failed: {error}')
            try:
                if bundle_id:
                    subprocess.run(['xcrun', 'simctl', 'terminate', udid, bundle_id], timeout=15,
                                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            except (OSError, subprocess.TimeoutExpired) as error:
                cleanup_errors.append(str(error))
            finally:
                try:
                    stop_process(launch)
                except (OSError, subprocess.TimeoutExpired) as error:
                    cleanup_errors.append(str(error))
        # The simulator was created by this run, so never shut down a user's device.
        try:
            subprocess.run(['xcrun', 'simctl', 'shutdown', udid], timeout=30,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except (OSError, subprocess.TimeoutExpired) as error:
            cleanup_errors.append(str(error))
        finally:
            try:
                subprocess.run(['xcrun', 'simctl', 'delete', udid], timeout=30, check=True)
            except (OSError, subprocess.SubprocessError) as error:
                cleanup_errors.append(str(error))
        if cleanup_errors:
            message = f'Owned Simulator {udid} cleanup failed: ' + '; '.join(cleanup_errors)
            log.error(message)
            if not primary_error:
                raise RuntimeError(message)
