# -----------------------------------------------------------
# Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
# This product includes software developed at Datadog (https://www.datadoghq.com/).
# Copyright 2025-Present Datadog, Inc.
# -----------------------------------------------------------

"""
Fetches OpenFeature and its transitive dependencies via `dotnet publish` and
copies the resulting netstandard2.0 DLLs into the Flags plugin directory.

Usage:
    ./run-script install_openfeature_dlls
    ./run-script install_openfeature_dlls --openfeature-version 2.11.1
    ./run-script install_openfeature_dlls --dry-run
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile

__repo_root__ = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

__flags_plugins_dir__ = os.path.join(
    __repo_root__,
    'packages', 'Datadog.Unity', 'Runtime', 'Flags', 'Plugins',
)

__asmdef_path__ = os.path.join(
    __repo_root__,
    'packages', 'Datadog.Unity', 'Runtime', 'Flags',
    'com.datadoghq.unity.flags.asmdef',
)

# DLLs that Unity already provides in its managed runtime.
# Including duplicates causes version-conflict errors at build time.
UNITY_BUNDLED_DLLS = {
    'System.Buffers.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll',
    'System.Threading.Tasks.Extensions.dll',
}

CSPROJ_TEMPLATE = """\
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>8.0</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="OpenFeature" Version="{version}" />
  </ItemGroup>
</Project>
"""


def _check_dotnet() -> None:
    if shutil.which('dotnet') is None:
        print('error: `dotnet` not found on PATH. Install the .NET SDK from https://dotnet.microsoft.com/download', file=sys.stderr)
        sys.exit(1)


def _publish(version: str, work_dir: str) -> str:
    """Writes a minimal .csproj, restores, and publishes; returns the publish output dir."""
    csproj_path = os.path.join(work_dir, 'of_export.csproj')
    with open(csproj_path, 'w') as f:
        f.write(CSPROJ_TEMPLATE.format(version=version))

    publish_dir = os.path.join(work_dir, 'publish')
    cmd = [
        'dotnet', 'publish', csproj_path,
        '-c', 'Release',
        '-o', publish_dir,
        '--nologo',
        '-v', 'minimal',
    ]
    print(f'Running: {" ".join(cmd)}')
    result = subprocess.run(cmd, capture_output=False)
    if result.returncode != 0:
        print('error: dotnet publish failed.', file=sys.stderr)
        sys.exit(result.returncode)

    return publish_dir


def _collect_dlls(publish_dir: str) -> list[str]:
    """Returns paths to all DLLs in publish_dir, excluding the stub project DLL."""
    dlls = []
    for fname in sorted(os.listdir(publish_dir)):
        if not fname.endswith('.dll'):
            continue
        if fname == 'of_export.dll':
            continue
        dlls.append(os.path.join(publish_dir, fname))
    return dlls


def _update_asmdef(dll_names: list[str]) -> None:
    """Adds the installed DLL names to precompiledReferences in the flags asmdef."""
    with open(__asmdef_path__, 'r') as f:
        asmdef = json.load(f)

    existing = set(asmdef.get('precompiledReferences', []))
    merged = sorted(existing | set(dll_names))
    asmdef['precompiledReferences'] = merged
    asmdef['overrideReferences'] = True

    with open(__asmdef_path__, 'w') as f:
        json.dump(asmdef, f, indent=4)
        f.write('\n')

    print(f'Updated {os.path.relpath(__asmdef_path__, __repo_root__)}')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--openfeature-version', default='2.11.1', metavar='VERSION',
                        help='NuGet version of OpenFeature to install (default: 2.11.1)')
    parser.add_argument('--dry-run', action='store_true',
                        help='Print what would be copied without making changes')
    args = parser.parse_args()

    _check_dotnet()

    with tempfile.TemporaryDirectory(prefix='dd_openfeature_') as work_dir:
        publish_dir = _publish(args.openfeature_version, work_dir)
        all_dlls = _collect_dlls(publish_dir)

        to_install = [p for p in all_dlls if os.path.basename(p) not in UNITY_BUNDLED_DLLS]
        skipped = [p for p in all_dlls if os.path.basename(p) in UNITY_BUNDLED_DLLS]

        if skipped:
            print('\nSkipping Unity-bundled DLLs (already provided by the engine):')
            for p in skipped:
                print(f'  {os.path.basename(p)}')

        print(f'\nDLLs to install into {os.path.relpath(__flags_plugins_dir__, __repo_root__)}:')
        for p in to_install:
            print(f'  {os.path.basename(p)}')

        if args.dry_run:
            print('\n--dry-run: no files written.')
            return

        os.makedirs(__flags_plugins_dir__, exist_ok=True)

        installed_names = []
        for src in to_install:
            fname = os.path.basename(src)
            dst = os.path.join(__flags_plugins_dir__, fname)
            shutil.copy2(src, dst)
            installed_names.append(fname)
            print(f'  copied {fname}')

        _update_asmdef(installed_names)

    print(f'\nDone. {len(installed_names)} DLL(s) installed.')
    print('Open the project in Unity to generate .meta files for the new plugins.')


if __name__ == '__main__':
    main()
