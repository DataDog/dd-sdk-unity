#!/usr/bin/env python3
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2023-Present Datadog, Inc.

"""Build the dd-sdk-cpp native shared library and copy it into the Unity Plugins directory."""

import argparse
import os
import platform
import shutil
import subprocess
import sys


REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
NATIVE_DIR = os.path.join(REPO_ROOT, 'native')
BUILD_DIR = os.path.join(REPO_ROOT, 'build', 'desktop')
PLUGINS_BASE = os.path.join(
    REPO_ROOT, 'packages', 'Datadog.Unity', 'Plugins', 'Desktop'
)
SUBMODULE_PATH = os.path.join(REPO_ROOT, 'modules', 'dd-sdk-cpp')


def detect_platform():
    system = platform.system()
    if system == 'Darwin':
        return 'mac'
    elif system == 'Windows':
        return 'windows'
    elif system == 'Linux':
        return 'linux'
    else:
        sys.exit(f'Unsupported platform: {system}')


def check_submodule():
    cmake_file = os.path.join(SUBMODULE_PATH, 'CMakeLists.txt')
    if not os.path.exists(cmake_file):
        sys.exit(
            'modules/dd-sdk-cpp is not initialized. '
            'Run: git submodule update --init'
        )


def run(cmd, **kwargs):
    print(f'$ {" ".join(cmd)}')
    subprocess.run(cmd, check=True, **kwargs)


def build(target_platform):
    os.makedirs(BUILD_DIR, exist_ok=True)

    cmake_args = [
        'cmake',
        '-S', NATIVE_DIR,
        '-B', BUILD_DIR,
        '-DCMAKE_BUILD_TYPE=Release',
    ]
    if target_platform == 'windows':
        cmake_args.append('-DDD_HTTP_USE_SYSTEM_LIBCURL=OFF')
    run(cmake_args)
    run(['cmake', '--build', BUILD_DIR, '--config', 'Release'])


def copy_output(target_platform):
    # The dd-sdk-cpp CMake target is named 'ddsdkcpp'; we rename it to 'dd_native'
    # so that [DllImport("dd_native")] resolves correctly in Unity.
    if target_platform == 'mac':
        src = os.path.join(BUILD_DIR, 'dd-sdk-cpp', 'src', 'libddsdkcpp.dylib')
        dst_dir = os.path.join(PLUGINS_BASE, 'macOS')
        dst = os.path.join(dst_dir, 'dd_native.dylib')
    elif target_platform == 'windows':
        src = os.path.join(BUILD_DIR, 'dd-sdk-cpp', 'src', 'Release', 'ddsdkcpp.dll')
        dst_dir = os.path.join(PLUGINS_BASE, 'Windows')
        dst = os.path.join(dst_dir, 'dd_native.dll')
    else:  # linux
        src = os.path.join(BUILD_DIR, 'dd-sdk-cpp', 'src', 'libddsdkcpp.so')
        dst_dir = os.path.join(PLUGINS_BASE, 'Linux')
        dst = os.path.join(dst_dir, 'dd_native.so')

    if not os.path.exists(src):
        sys.exit(f'Build output not found: {src}')

    os.makedirs(dst_dir, exist_ok=True)
    shutil.copy2(src, dst)
    print(f'Copied {src} -> {dst}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        '--platform',
        choices=['mac', 'windows', 'linux'],
        default=None,
        help='Target platform (defaults to host platform)',
    )
    args = parser.parse_args()

    target_platform = args.platform or detect_platform()
    print(f'Building for platform: {target_platform}')

    check_submodule()
    build(target_platform)
    copy_output(target_platform)
    print('Done.')


if __name__ == '__main__':
    main()
