import os
import platform
from typing import Tuple


def resolve_android_binary(*args: str) -> Tuple[str, str]:
    android_home = os.getenv('ANDROID_HOME') or ''
    if not android_home:
        return '', 'ANDROID_HOME is not set'

    package_dirname, relpath = args[0], args[1:]
    package_dirpath = os.path.join(android_home, package_dirname)
    if not os.path.isdir(package_dirpath):
        return '', f'Android {package_dirname} package is not installed'
    
    binary_filepath = os.path.join(package_dirpath, *relpath)
    if platform.system() == 'Windows':
        binary_filepath += '.exe'
    if not os.path.isfile(binary_filepath):
        return '', f'File not found: {binary_filepath}'
    
    return binary_filepath, ''
