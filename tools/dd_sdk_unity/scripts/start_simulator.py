# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Starts an emulated device for either iOS or Android, blocking until Ctrl-C is pressed.
For Unity Editor tests, --action start reuses a connected Android device or starts
an emulator and prints its serial. --action stop --serial SERIAL stops that emulator.
"""
import argparse
import subprocess
import time
from contextlib import ExitStack

from dd_sdk_unity.scripts.common import MobilePlatform
from dd_sdk_unity.scripts.common.android import Adb
from dd_sdk_unity.scripts.common.android.adb import AdbDevice
from dd_sdk_unity.scripts.common.log import init_logger
from dd_sdk_unity.scripts.common.simulator import run_default_simulator


def start_simulator(platform: MobilePlatform) -> int:
    log = init_logger()
    with run_default_simulator(platform):
        log.info('Simulator running.')
        log.info('Press Ctrl-C to exit...')
        while True:
            try:
                time.sleep(0)
            except KeyboardInterrupt:
                log.info('')
                break
    return 0


def start_android_simulator() -> None:
    log = init_logger()
    adb = Adb.require()
    devices = adb.list_devices()
    if devices:
        adb.start_server()
        device = adb.wait_for_device(devices[0].name, timeout_seconds=120)
        device.wait_for_boot(timeout_seconds=120)
        log.info(f'Reusing Android device {device.name}.')
        return
    with run_default_simulator(
        'android', keep_running=True, on_started=lambda serial: print(serial, flush=True),
    ):
        pass


def stop_android_simulator(serial: str) -> None:
    if not serial.startswith('emulator-'):
        return
    adb = Adb.require()
    try:
        # An explicit serial addresses the emulator console even before ADB is online.
        AdbDevice(adb.path, serial).emu_kill()
    except subprocess.CalledProcessError:
        output = subprocess.check_output([adb.path, 'devices'], text=True, timeout=10)
        if any(line.split()[:1] == [serial] for line in output.splitlines()):
            raise


def connect_android_editor(port: int) -> None:
    devices = Adb.require().list_devices()
    if not devices:
        raise RuntimeError('No Android device is available for the Editor connection')
    serials = []
    with ExitStack() as rollback:
        for device in devices:
            if device.reverse_port(port):
                rollback.callback(device.remove_reverse_port, port)
                serials.append(device.name)
        if serials:
            print('\n'.join(serials), flush=True)
        rollback.pop_all()


def disconnect_android_editor(serial: str, port: int) -> None:
    for device in Adb.require().list_devices():
        if device.name == serial:
            device.remove_reverse_port(port)
            return


def main() -> int:
    parser = argparse.ArgumentParser(description='Boots up an emulated mobile device for testing')
    parser.add_argument('--platform', choices=['ios', 'android'], required=True)
    parser.add_argument('--action', choices=['start', 'stop', 'connect', 'disconnect'])
    parser.add_argument('--serial', help='Device serial to stop or disconnect')
    parser.add_argument('--editor-port', type=int, help='Editor PlayerConnection port')
    args = parser.parse_args()

    if args.action:
        if args.platform != 'android':
            parser.error('--action requires --platform android')
        if args.action in ('stop', 'disconnect') and not args.serial:
            parser.error(f'--action {args.action} requires --serial')
        if args.action in ('connect', 'disconnect') and (
                args.editor_port is None or not 1 <= args.editor_port <= 65535):
            parser.error(f'--action {args.action} requires a valid --editor-port')
        if args.action == 'stop':
            stop_android_simulator(args.serial)
        elif args.action == 'connect':
            connect_android_editor(args.editor_port)
        elif args.action == 'disconnect':
            disconnect_android_editor(args.serial, args.editor_port)
        else:
            start_android_simulator()
        return 0

    return start_simulator(args.platform)
