import os
import sys
import subprocess
import time
import signal
import threading
from dataclasses import dataclass
from contextlib import contextmanager
from typing import Optional, Generator

from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.chrome.options import Options

from common.log import get_default_logger
from common.unity import UnityBuildPlatform, UnityBuild
from common.shell import OutputHandlerFunc
from common.android import run_android_device, Adb, __default_android_device__
from common.apple import run_apple_device, Xcrun, IDeviceSyslog, __default_ios_device__


@dataclass
class LaunchConfig:
    build: UnityBuild
    use_simulator: bool
    output_func: Optional[OutputHandlerFunc]


@contextmanager
def launch_build(platform: UnityBuildPlatform, config: LaunchConfig) -> Generator[None, None, None]:
    if platform == UnityBuildPlatform.ANDROID:
        with _launch_android_build(config):
            yield
    elif platform == UnityBuildPlatform.IOS:
        with _launch_ios_build(config):
            yield
    elif platform == UnityBuildPlatform.WEBGL:
        with _launch_webgl_build(config):
            yield
    else:
        raise RuntimeError('Unsupported platform for launch_build')


@contextmanager
def _launch_android_build(config: LaunchConfig) -> Generator[None, None, None]:
    # Get an Android device, either emulated or physical, on which to run the build
    log = get_default_logger()
    with _acquire_android_device(config.use_simulator) as adb_device_name:
        # Install the build, making sure any old build is removed first
        adb = Adb.require()

        log.info(f'Installing {config.build.app_bundle_id} to ADB device {adb_device_name}...')
        adb.uninstall(adb_device_name, config.build.app_bundle_id)
        adb.install(adb_device_name, config.build.app_bundle_id, config.build.app_bundle_path)

        # Tail logcat output to our script logger and to the provided handler func
        filters = [
            'Unity:V',
            'IL2CPP:V',
            'Datadog:V',
            'OkHttp:V',
            'System.err:V',
            'AndroidRuntime:E',
            '*:S',
        ]
        adb.tail_logs(adb_device_name, filters, config.output_func)

        # If we launch the build prematurely, Android will boot us back to the home
        # screen, and there's no easy way of reliably determining when a newly-installed
        # build has "settled"
        #if config.use_simulator:
            #log.info('Waiting for a brief moment post-install...')
            #time.sleep(4.0)

        # Launch the installed build
        log.info(f'Launching {config.build.app_bundle_id} on ADB device {adb_device_name}...')
        adb.launch(adb_device_name, config.build.app_bundle_id, 'com.unity3d.player.UnityPlayerActivity')
        yield


@contextmanager
def _acquire_android_device(use_simulator: bool) -> Generator[str, None, None]:
    if use_simulator:
        # For simulator, use avdmanager/emulator/etc. to start up an AVD
        with run_android_device(__default_android_device__) as adb_device_name:
            yield adb_device_name
    else:
        # Use 'adb devices' to get a list of all connected Android devices that are ready
        adb = Adb.require()
        devices = adb.list_devices()
        devices = adb.list_devices()
        if not devices:
            raise RuntimeError('adb devices lists no devices with ready (i.e. "device") status')

        # Take the first device that isn't an emulator
        device = next((d for d in devices if not d.name.startswith('emulator')), None)
        if not device:
            raise RuntimeError('No physical Android devices are connected and ready')

        yield device.name


@contextmanager
def _launch_ios_build(config: LaunchConfig) -> Generator[None, None, None]:
    # Get an iOS device to run on
    log = get_default_logger()
    with _acquire_ios_device(config.use_simulator) as udid:
        # Install the build, making sure any old build is removed
        xcrun = Xcrun.require()
        log.info(f'Installing {config.build.app_bundle_id} to iOS device {udid}...')
        if config.use_simulator:
            xcrun.simctl.uninstall(udid, config.build.app_bundle_id)
            xcrun.simctl.install(udid, config.build.app_bundle_path)
        else:
            xcrun.devicectl.uninstall(udid, config.build.app_bundle_id)
            xcrun.devicectl.install(udid, config.build.app_bundle_path)

        # Tail log output to our script logger and to the provided handler func
        if config.use_simulator:
            predicate = 'senderImagePath CONTAINS[c] "UnityFramework"'
            xcrun.simctl.tail_logs(udid, predicate, config.output_func)
        else:
            idevicesyslog = IDeviceSyslog.require()
            idevicesyslog.run(udid, 'UnityFramework', config.output_func)

        # Launch the installed build
        log.info(f'Launching {config.build.app_bundle_id} on iOS device {udid}...')
        if config.use_simulator:
            xcrun.simctl.launch(udid, config.build.app_bundle_id)
        else:
            xcrun.devicectl.launch(udid, config.build.app_bundle_id)
        yield


@contextmanager
def _acquire_ios_device(use_simulator: bool) -> Generator[str, None, None]:
    if use_simulator:
        # For simulator, use xcrun simctl
        with run_apple_device(__default_ios_device__) as udid:
            yield udid
    else:
        # Use 'xcrun devicectl list devices' to get a list of physical iOS devices
        xcrun = Xcrun.require()
        devices = xcrun.devicectl.list_devices()
        if not devices:
            raise RuntimeError('xcrun devicectl reports no available devices!')

        # Take the first device that's paired, i.e. ready to use
        device = next((d for d in devices if d.connection_properties.pairing_state == 'paired'), None)
        if not device:
            raise RuntimeError('No available iOS devices are paired')
        
        yield device.hardware_properties.udid


@contextmanager
def _launch_webgl_build(config: LaunchConfig) -> Generator[None, None, None]:
    assert config.build.app_bundle_path.endswith('index.html')
    assert config.build.app_bundle_id == 'WebGL'

    done = threading.Event()
    lock = threading.Lock()

    # Run an HTTP server in a subprocess to serve our Unity Web Player bundle
    rootdir = os.path.dirname(config.build.app_bundle_path)
    port = 8787
    with _run_http_server(rootdir, port) as url:
        # Use Selenium to drive an instance of Chrome
        options = Options()
        options.set_capability('goog:loggingPrefs', {'browser': 'ALL'})
        options.add_argument('--disable-background-timer-throttling')
        options.add_argument('--disable-backgrounding-occluded-windows')
        driver = webdriver.Chrome(options)

        def _log_main():
            log = get_default_logger()
            while not done.is_set():
                try:
                    with lock:
                        new_logs = driver.get_log('browser')

                    if new_logs:
                        for item in new_logs:
                            line = item['message']
                            log.info(line)
                            if config.output_func:
                                config.output_func(line, False)
                    else:
                        time.sleep(0.1)
                except Exception as e:
                    log.error(f'Error while tailing Chrome output: {e}')
                    time.sleep(1.0)

        thread = threading.Thread(target=_log_main, daemon=True)
        thread.start()            

        # Browse to our Unity Web Player's index page, thereby launching the build
        driver.get(url)

        # Click on the Unity canvas to silence autoplay warnings
        elem = driver.find_element(By.ID, 'unity-canvas')
        if not elem:
            raise RuntimeError('#unity-canvas not found!')
        elem.click()

        yield
    
    done.set()


@contextmanager
def _run_http_server(rootdir: str, port: int) -> Generator[str, None, None]:
    """
    Uses the current Python interpreter binary to run an HTTP server in a subprocess,
    binding to the given port and serving files from the specified directory.
    """
    args = [sys.executable, '-m', 'http.server', str(port)]
    p = subprocess.Popen(args, cwd=rootdir)
    try:
        yield f'http://127.0.0.1:{port}'
    finally:
        p.send_signal(signal.SIGTERM)
        p.wait()
