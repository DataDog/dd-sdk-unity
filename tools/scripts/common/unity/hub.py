"""
Utility code for finding and invoking Unity Hub, which should be installed system-wide
and which is used to manage installations of the Unity Editor.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import platform
from typing import List, Optional

from common.log import get_default_logger
from common.shell import run_cmd

from .install import UnityInstall, UnityVersion


class UnityHub(object):
    """Wrapper for the Unity Hub binary."""
    path: str

    def __init__(self, path: str):
        self.path = path
    
    def list_installs(self) -> List[UnityInstall]:
        """
        Runs Unity Hub in headless mode to obtain a list of all Unity editor
        installations on this system.
        """
        log = get_default_logger()
        log.info('Finding Unity installations...')
        installs: List[UnityInstall] = []
        def _read(line: str, _):
            install = UnityInstall.parse(line)
            if install:
                log.info(f'Found: {install.version} at {install.path}')
                installs.append(install)
            else:
                log.warning(f'Unexpected output format: {line}')
        run_cmd(
            self.path, '--', '--headless', 'editors', '--installed',
            raise_on_nonzero_exitcode=True,
            output_handler=_read,
        )
        return installs
    
    def list_release_versions(self) -> List[UnityVersion]:
        """
        Runs Unity Hub in headless mode to obtain a list of all Unity editor
        versions that can be installed to this system.
        """
        log = get_default_logger()
        log.info('Querying available Unity releases...')
        versions: List[UnityVersion] = []
        def _read(line: str, _):
            version = UnityVersion.parse(line.split()[0])
            if version:
                log.info(f'Available: {version}')
                versions.append(version)
            else:
                log.warning(f'Unexpected output format: {line}')
        run_cmd(
            self.path, '--', '--headless', 'editors', '--releases',
            raise_on_nonzero_exitcode=True,
            output_handler=_read,
        )
        return versions
    
    def install_version(self, version: UnityVersion, modules: List[str]) -> UnityInstall:
        log = get_default_logger()
        log.info(f'Installing Unity {version}...')

        args = [self.path, '--', '--headless', 'install', '--version', str(version)]
        args.append('--module')
        args.extend(modules)
        args.append('--childModules')
        if platform.system() == 'Darwin':
            args.append('--architecture')
            args.append('arm64' if platform.processor() == 'arm' else 'x86_64')
        
        run_cmd(*args, raise_on_nonzero_exitcode=True, echo=True)

        installs = self.list_installs()
        new_version = next((x for x in installs if x.version == version), None)
        if not new_version:
            raise RuntimeError('Failed to resolve Unity install after successful completion of install command')
        return new_version

    def install_modules(self, version: UnityVersion, modules: List[str]) :
        log = get_default_logger()
        log.info(f'Installing Unity {version}...')

        args = [self.path, '--', '--headless', 'install-modules', '--version', str(version)]
        for module in modules:
            args.append('--module')
            args.append(module)
        args.append('--childModules')
        if platform.system() == 'Darwin':
            args.append('--architecture')
            args.append('arm64' if platform.processor() == 'arm' else 'x86_64')

        # Don't worry if this fails. It may mean all modules are installed
        run_cmd(*args, raise_on_nonzero_exitcode=False, echo=True)

    @classmethod
    def require(cls) -> 'UnityHub':
        """
        Locates the Unity Hub binary on this system, raising an error if not found.
        """
        found = cls.find()
        if not found:
            raise RuntimeError('Unity Hub binary not found')
        return found

    @classmethod
    def find(cls) -> Optional['UnityHub']:
        """
        Locates the Unity Hub binary on this system, returning None if not found.
        """
        log = get_default_logger()

        # Assemble candidate binary paths based on OS
        paths: List[str] = []
        system = platform.system()
        log.debug(f'Detected OS: {system}')
        if system == 'Darwin':
            paths.append('/Applications/Unity Hub.app/Contents/MacOS/Unity Hub')
        elif system == 'Windows':
            program_files = os.getenv('ProgramFiles', os.path.normpath('C:/Program Files'))
            paths.append(os.path.join(program_files, 'Unity Hub', 'UnityHub.exe'))
            paths.append(os.path.expandvars(os.path.normpath('%LOCALAPPDATA%/Microsoft/WindowsApps/unityhub.exe')))
        else:
            paths.append('/usr/bin/unityhub')
            paths.append('/usr/local/bin/unityhub')

        # Check for the binary at each path and return it once found
        for path in paths:
            log.debug(f'Checking path: {path}')
            if os.path.isfile(path):
                log.info(f'Found Unity Hub binary at: {path}')
                return cls(path)

        # We could not detect a Unity Hub installation
        return None
