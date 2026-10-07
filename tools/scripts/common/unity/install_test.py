"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import pytest

import os
import subprocess
import sys

from .install import UnityVersion, UnityInstall, resolve_unity_install, match_unity_version


def test_UnityVersion_comparison():
    assert UnityVersion.parse('6000.1.7f1') == UnityVersion(
        major=6000,
        minor=1,
        patch=7,
        revision='f1',
    )
    assert UnityVersion.parse('2022.3.55f1') < UnityVersion.parse('6000.1.7f1')

    ordered = list(sorted([
        UnityVersion.parse('2022.3.55f1'),
        UnityVersion.parse('2022.4.22b3'),
        UnityVersion.parse('6000.1.7f1'),
        UnityVersion.parse('2022.4.22a4'),
    ]))
    assert [str(x) for x in ordered] == [
        '2022.3.55f1',
        '2022.4.22a4',
        '2022.4.22b3',
        '6000.1.7f1',
    ]


@pytest.mark.parametrize('path, expected', [
    (
        '/Applications/Unity/Hub/Editor/6000.1.7f1/Unity.app',
        '/Applications/Unity/Hub/Editor/6000.1.7f1/Unity.app/Contents/MacOS/Unity',
    ),
    (
        os.path.normpath('C:/Program Files/Unity/Hub/Editor/6000.1.7f1/Editor/Unity.exe'),
        os.path.normpath('C:/Program Files/Unity/Hub/Editor/6000.1.7f1/Editor/Unity.exe'),
    ),
    (
        '/home/username/Unity/Hub/Editor/6000.1.7f1/Editor/Unity',
        '/home/username/Unity/Hub/Editor/6000.1.7f1/Editor/Unity',
    ),
])
def test_UnityInstall_editor_path(path, expected):
    install = UnityInstall(
        version=UnityVersion.parse('6000.1.7f1'),
        architecture='arm64',
        path=path,
    )
    assert install.editor_path == expected


def test_resolve_unity_install():
    unity_6000 = UnityInstall(
        version=UnityVersion.parse('6000.1.7f1'),
        architecture='arm64',
        path='/Applications/Unity/Hub/Editor/6000.1.7f1/Unity.app',
    )
    unity_2022 = UnityInstall(
        version=UnityVersion.parse('2022.3.55f1'),
        architecture='arm64',
        path='/Applications/Unity/Hub/Editor/2022.3.55f1/Unity.app',
    )
    installs = [unity_6000, unity_2022]

    assert resolve_unity_install(installs, '2022') == unity_2022
    assert resolve_unity_install(installs, '6000') == unity_6000
    assert resolve_unity_install(installs, '6000.1.7') == unity_6000
    assert resolve_unity_install(installs, '6000.2') is None
    assert resolve_unity_install(installs, '2021') is None


def test_match_unity_version():
    versions = [UnityVersion.parse(s) for s in [
        '2022.3.0f1',
        '2022.3.0f2',
        '6000.0.62f1',
        '6000.0.62f2',
        '2022.3.55f1',
        '2022.3.11rc1',
        '2022.2.0f1',
        '6000.1.7f1',
        '6000.1.7p2',
    ]]
    for [version_prefix, want] in [
        ['2022', '2022.3.55f1'],
        ['2022.2', '2022.2.0f1'],
        ['2022.3', '2022.3.55f1'],
        ['2022.3.11', '2022.3.11rc1'],
        ['6000', '6000.1.7p2'],
        ['6000.1.7f1', '6000.1.7f1'],
        ['6000.0', '6000.0.62f2'],
        ['6000.0.62', '6000.0.62f2'],
        ['6000.0.62f1', '6000.0.62f1'],
        ['2022.3.0', '2022.3.0f2'],
        ['2022.3.0f1', '2022.3.0f1'],
    ]:
        got = match_unity_version(versions, version_prefix)
        assert got
        assert got == want

    assert match_unity_version(versions, '6000.0.0') is None
    assert match_unity_version(versions, '2022.3.0p1') is None


@pytest.mark.parametrize('ci', [False, True])
def test_run_batchmode_preserves_log_and_exit_status(tmp_path, monkeypatch, capsys, ci):
    log_path = tmp_path / 'Editor.log'
    contents = ('Assets/Test.cs(1,1): warning CS0168: unused variable\n'
                + 'Falling back to CPU lightmapper.\n' * 100
                + 'Build progress\nAssets/Test.cs(2,1): error CS0103: unknown name\n')
    real_popen = subprocess.Popen
    launches = []

    def popen(command, *args, **kwargs):
        launches.append((command, kwargs))
        return real_popen(
            [sys.executable, '-c',
             'from pathlib import Path; import sys; Path(sys.argv[1]).write_text(sys.argv[2]); raise SystemExit(2)',
             str(log_path), contents], *args, **kwargs,
        )
    monkeypatch.setattr(subprocess, 'Popen', popen)
    monkeypatch.setenv('CI', 'true' if ci else 'false')

    install = UnityInstall(UnityVersion.parse('2022.3.67f2'), 'arm64', '/fake/Unity')
    result = install.run_batchmode('/fake/sample project', '-runTests', log_path=str(log_path), timeout_seconds=10)

    assert result == 2
    assert log_path.read_text() == contents
    console = capsys.readouterr()
    assert 'Build progress' in console.out
    assert 'error CS0103' in console.out
    assert console.out.count('Falling back to CPU lightmapper.') == (1 if ci else 100)
    assert ('warning CS0168' in console.out) == (not ci)
    assert launches[0][0][1] == '-batchmode'
    assert 'stdout' not in launches[0][1]
    assert 'stderr' not in launches[0][1]
