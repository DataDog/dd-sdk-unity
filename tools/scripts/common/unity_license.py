import os
import platform
import ctypes
from contextlib import contextmanager
from typing import Generator

from common.log import get_default_logger
from common.unity_install import UnityInstall
from common.shell import run_cmd


def _get_unity_config_path() -> str:
    system = platform.system()
    if system == 'Darwin':
        return '/Library/Application Support/Unity/config/'
    elif system == 'Windows':
        return os.path.expandvars('%PROGRAMDATA%\\Unity\\config')
    else:
        return '/usr/share/unity3d/config/'


@contextmanager
def require_unity_license(install: UnityInstall) -> Generator[None, None, None]:
    log = get_default_logger()

    log.info(f'Checking license status for Unity {install.version}...')
    if _install_is_licensed(install):
        log.info('Unity license OK; no need to acquire a floating license.')
    else:
        log.info('Unity license not installed; attempting to obtain a floating license...')

    # TODO: Unity, man
    yield


def _install_is_licensed(install: UnityInstall) -> bool:
    exitcode = run_cmd(install.editor_path, '-batchmode', '-quit' ,'-version')
    return exitcode == 0
