"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import pytest

import os

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
