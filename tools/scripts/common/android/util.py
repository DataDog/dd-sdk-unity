"""
Helpers for Android tool wrappers.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import platform
from typing import Tuple


def resolve_android_binary(*args: str) -> Tuple[str, str]:
    package_dirname, relpath = args[0], args[1:]
    package_dirpath, err_message = _resolve_android_sdk_package_dir(package_dirname)
    if err_message:
        return '', err_message

    binary_filepath, err_message = _resolve_executable(os.path.join(package_dirpath, *relpath))
    if err_message:
        return '', err_message

    return binary_filepath, ''


def resolve_cmdline_tools_binary(name: str) -> Tuple[str, str]:
    package_dirpath, err_message = _resolve_android_sdk_package_dir('cmdline-tools')
    if err_message:
        return '', err_message

    subdirs = [f for f in os.listdir(package_dirpath) if os.path.isdir(os.path.join(package_dirpath, f))]
    version_dir = 'latest'
    if version_dir not in subdirs:
        subdirs.sort()
        version_dir = subdirs[-1]

    binary_filepath, err_message = _resolve_executable(os.path.join(package_dirpath, version_dir, 'bin', name))
    if err_message:
        return '', err_message

    return binary_filepath, ''


def _resolve_android_sdk_package_dir(package_dirname: str) -> Tuple[str, str]:
    android_home = os.getenv('ANDROID_HOME') or ''
    if not android_home:
        return '', 'ANDROID_HOME is not set'

    package_dirpath = os.path.join(android_home, package_dirname)
    if not os.path.isdir(package_dirpath):
        return '', f'Android {package_dirname} package is not installed'
    
    return package_dirpath, ''


def _resolve_executable(filepath_noext: str) -> Tuple[str, str]:
    filepath = filepath_noext
    if platform.system() == 'Windows':
        filepath += '.exe'
    if not os.path.isfile(filepath):
        return '', f'File not found: {filepath}'
    return filepath, ''
