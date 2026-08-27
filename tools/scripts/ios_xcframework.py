#!/usr/bin/env python3
# Unless explicitly stated otherwise all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""
Fetches Datadog's officially published, prebuilt dd-sdk-ios XCFramework bundle at the
version pinned in packages/Datadog.Unity/Editor/iOS/IosDependencyVersion.json, verifies
its SHA-256 digest, and stages the pinned module subset directly into the package's own
Plugins/iOS directory (packages/Datadog.Unity/Plugins/iOS/) — the location every UPM
consumer sees, not a per-consuming-project staging path.

Usage (via the repo's run-script wrapper):
    ./run-script ios_xcframework stage [--version V] [--force] [--allow-unknown-sha256]
    ./run-script ios_xcframework verify
    ./run-script ios_xcframework clean
"""

import argparse
import hashlib
import os
import re
import shutil
import subprocess
import sys
import zipfile

from common.log import init_logger
from common.versions.ios_xcframework_deps import (
    IOS_DEPENDENCY_VERSION_RELPATH,
    read_ios_xcframework_pin,
)


REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PIN_PATH = os.path.join(REPO_ROOT, IOS_DEPENDENCY_VERSION_RELPATH)
PLUGINS_IOS_DIR = os.path.join(REPO_ROOT, 'packages', 'Datadog.Unity', 'Plugins', 'iOS')
WORK_DIR = os.path.join(REPO_ROOT, 'build', 'ios-xcframework')
DOWNLOAD_DIR = os.path.join(WORK_DIR, 'downloads')
EXTRACT_DIR = os.path.join(WORK_DIR, 'extracted')

# Name of the top-level directory inside dd-sdk-ios's release zip (holds one
# <Module>.xcframework subdirectory per module).
_XCFRAMEWORK_ZIP_ROOT = 'Datadog.xcframework'
_XCFRAMEWORK_ZIP_FILENAME = f'{_XCFRAMEWORK_ZIP_ROOT}.zip'


def zip_path_for(version):
    # Cached per-version so a stale zip from a previously staged version is never
    # mistaken for the one currently requested (see fetch()/cmd_stage()).
    return os.path.join(DOWNLOAD_DIR, str(version), _XCFRAMEWORK_ZIP_FILENAME)

RELEASE_URL_TEMPLATE = (
    'https://github.com/DataDog/dd-sdk-ios/releases/download/{version}/' + _XCFRAMEWORK_ZIP_FILENAME
)

_XCFRAMEWORK_ENTRY_RE = re.compile(rf'^{re.escape(_XCFRAMEWORK_ZIP_ROOT)}/([^/]+)\.xcframework/$')


def check_tools():
    missing = [tool for tool in ('curl', 'unzip') if shutil.which(tool) is None]
    if missing:
        sys.exit(f'Required tool(s) not found on PATH: {", ".join(missing)}. Please install them and retry.')


def load_pin():
    if not os.path.exists(PIN_PATH):
        sys.exit(f'{PIN_PATH} not found; cannot resolve the pinned dd-sdk-ios version.')
    with open(PIN_PATH, 'r', encoding='utf-8') as infile:
        contents = infile.read()
    return read_ios_xcframework_pin(contents)


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as infile:
        for chunk in iter(lambda: infile.read(65536), b''):
            digest.update(chunk)
    return digest.hexdigest()


def run(cmd, **kwargs):
    print(f'$ {" ".join(cmd)}')
    subprocess.run(cmd, check=True, **kwargs)


def run_capture(cmd):
    print(f'$ {" ".join(cmd)}')
    result = subprocess.run(cmd, check=True, capture_output=True, text=True)
    return result.stdout


def list_zip_modules(zip_path):
    output = run_capture(['unzip', '-l', zip_path])
    modules = []
    for line in output.splitlines():
        parts = line.split()
        if not parts:
            continue
        name = parts[-1]
        match = _XCFRAMEWORK_ENTRY_RE.match(name)
        if match:
            modules.append(match.group(1))
    return modules


def fetch(log, version, expected_sha256, force, allow_unknown_sha256):
    check_tools()
    zip_path = zip_path_for(version)
    os.makedirs(os.path.dirname(zip_path), exist_ok=True)
    url = RELEASE_URL_TEMPLATE.format(version=version)

    if os.path.exists(zip_path) and not force:
        log.info(f'{zip_path} already exists; skipping download (use --force to re-download).')
    else:
        # No shell involved; '=https' restricts curl to exactly the https:// protocol.
        run([
            'curl',
            '--proto', '=https',
            '--tlsv1.2',
            '--fail',
            '--location',
            '--silent',
            '--show-error',
            '-o', zip_path,
            url,
        ])

    digest = sha256_of(zip_path)
    log.info(f'sha256={digest}')

    if expected_sha256:
        if digest != expected_sha256:
            os.remove(zip_path)
            sys.exit(
                f'SHA-256 mismatch for {url}: expected {expected_sha256}, got {digest}. '
                'The downloaded zip was deleted; nothing was staged.'
            )
        log.info('SHA-256 verified against the pinned digest.')
    else:
        log.warning(f'No sha256 pinned for version {version}; computed digest is {digest}.')
        if not allow_unknown_sha256:
            sys.exit(
                'Refusing to continue without a pinned sha256. Pass --allow-unknown-sha256 to '
                'proceed anyway (e.g. when bumping to a version whose digest is not yet pinned).'
            )

    return digest


def stage(log, version, modules, plugins_ios_dir=None):
    plugins_ios_dir = plugins_ios_dir or PLUGINS_IOS_DIR
    zip_path = zip_path_for(version)
    if not os.path.exists(zip_path):
        sys.exit(f'{zip_path} not found. Run "fetch" first.')

    if os.path.isdir(EXTRACT_DIR):
        shutil.rmtree(EXTRACT_DIR)
    os.makedirs(EXTRACT_DIR, exist_ok=True)

    with zipfile.ZipFile(zip_path) as zf:
        namelist = zf.namelist()
        bundle_inventory = list_zip_modules(zip_path)
        for module in modules:
            prefix = f'{_XCFRAMEWORK_ZIP_ROOT}/{module}.xcframework/'
            members = [name for name in namelist if name.startswith(prefix)]
            if not members:
                sys.exit(
                    f'Module {module} not found in {zip_path}; bundle contains: {", ".join(bundle_inventory)}.'
                )
            for member in members:
                zf.extract(member, EXTRACT_DIR)

    # zf.extract() preserves the zip's top-level directory prefix; flatten it so
    # EXTRACT_DIR/<Module>.xcframework exists directly.
    extracted_root = os.path.join(EXTRACT_DIR, _XCFRAMEWORK_ZIP_ROOT)
    if os.path.isdir(extracted_root):
        for name in os.listdir(extracted_root):
            src = os.path.join(extracted_root, name)
            dst = os.path.join(EXTRACT_DIR, name)
            if os.path.exists(dst):
                shutil.rmtree(dst)
            shutil.move(src, dst)
        shutil.rmtree(extracted_root)

    os.makedirs(plugins_ios_dir, exist_ok=True)

    # Remove any previously staged module no longer in the requested set (e.g. after
    # `update_ios_version --modules` drops one). Unity discovers native plugins by
    # scanning this directory, independent of the JSON pin, so a stale bundle left here
    # would still be linked/embedded into iOS builds.
    wanted = {f'{module}.xcframework' for module in modules}
    for entry in os.listdir(plugins_ios_dir):
        if not entry.endswith('.xcframework') or entry in wanted:
            continue
        stale_path = os.path.join(plugins_ios_dir, entry)
        shutil.rmtree(stale_path)
        log.info(f'Removed stale module no longer in the requested set: {stale_path}')
        meta_path = f'{stale_path}.meta'
        if os.path.exists(meta_path):
            os.remove(meta_path)
            log.info(f'Removed {meta_path}')

    for module in modules:
        staged_src = os.path.join(EXTRACT_DIR, f'{module}.xcframework')
        if not os.path.isdir(staged_src):
            sys.exit(f'Expected staged module not found: {staged_src}')
        dst = os.path.join(plugins_ios_dir, f'{module}.xcframework')
        if os.path.exists(dst):
            shutil.rmtree(dst)
        shutil.copytree(staged_src, dst)
        log.info(f'Staged {dst}')


def verify(log, modules, plugins_ios_dir=None):
    plugins_ios_dir = plugins_ios_dir or PLUGINS_IOS_DIR
    failures = []
    for module in modules:
        module_path = os.path.join(plugins_ios_dir, f'{module}.xcframework')
        if not os.path.isdir(module_path):
            failures.append(f'{module}: missing directory {module_path}')
            continue
        info_plist = os.path.join(module_path, 'Info.plist')
        if not os.path.exists(info_plist):
            failures.append(f'{module}: missing {info_plist}')
            continue
        slices = [
            name for name in os.listdir(module_path)
            if name.startswith('ios-arm64') and os.path.isdir(os.path.join(module_path, name))
        ]
        if not slices:
            failures.append(f'{module}: no ios-arm64* slice directory found under {module_path}')
            continue
        log.info(f'{module}: OK ({module_path})')

    if failures:
        sys.exit('XCFramework verification failed:\n' + '\n'.join(f'  - {f}' for f in failures))


def clean(log):
    if os.path.isdir(WORK_DIR):
        shutil.rmtree(WORK_DIR)
        log.info(f'Removed {WORK_DIR}')
    else:
        log.info(f'{WORK_DIR} does not exist; nothing to remove.')

    if os.path.isdir(PLUGINS_IOS_DIR):
        for entry in os.listdir(PLUGINS_IOS_DIR):
            if not entry.endswith('.xcframework'):
                continue
            module_path = os.path.join(PLUGINS_IOS_DIR, entry)
            shutil.rmtree(module_path)
            log.info(f'Removed {module_path}')
            meta_path = f'{module_path}.meta'
            if os.path.exists(meta_path):
                os.remove(meta_path)
                log.info(f'Removed {meta_path}')


def cmd_stage(args, log):
    pin = load_pin()
    version = args.version or str(pin.version)
    expected_sha256 = pin.sha256 if not args.version or args.version == str(pin.version) else None
    # Always call fetch(): it rehashes and re-verifies the digest of a cached zip even
    # when it skips the network download, so a truncated or corrupted cache is never
    # staged silently.
    fetch(log, version, expected_sha256, args.force, args.allow_unknown_sha256)
    stage(log, version, pin.modules)
    verify(log, pin.modules)


def cmd_verify(args, log):
    pin = load_pin()
    verify(log, pin.modules)


def cmd_clean(args, log):
    clean(log)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    subparsers = parser.add_subparsers(dest='command', required=True)

    stage_parser = subparsers.add_parser(
        'stage', help='Fetch (if needed), extract, stage the pinned modules into Plugins/iOS, and verify.'
    )
    stage_parser.add_argument('--version', default=None, help='Override the dd-sdk-ios version to fetch.')
    stage_parser.add_argument('--force', action='store_true', help='Re-download even if the zip already exists.')
    stage_parser.add_argument(
        '--allow-unknown-sha256', action='store_true',
        help='Proceed even if no sha256 is pinned for the resolved version.',
    )
    stage_parser.set_defaults(func=cmd_stage)

    verify_parser = subparsers.add_parser(
        'verify', help='Validate that the pinned modules are staged in Plugins/iOS (no network).'
    )
    verify_parser.set_defaults(func=cmd_verify)

    clean_parser = subparsers.add_parser(
        'clean', help='Remove the download/extraction work directory and any staged modules.'
    )
    clean_parser.set_defaults(func=cmd_clean)

    args = parser.parse_args()
    log = init_logger()
    args.func(args, log)
    print('Done.')


if __name__ == '__main__':
    main()
