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


def test_UnityInstall_parse():
    mac_install = UnityInstall.parse('6000.1.7f1 (Apple silicon), installed at /Applications/Unity/Hub/Editor/6000.1.7f1/Unity.app')
    assert mac_install is not None
    assert mac_install == UnityInstall(
        version=UnityVersion(
            major=6000,
            minor=1,
            patch=7,
            revision='f1',
        ),
        architecture='Apple silicon',
        path='/Applications/Unity/Hub/Editor/6000.1.7f1/Unity.app'
    )
    assert mac_install.editor_path == '/Applications/Unity/Hub/Editor/6000.1.7f1/Unity.app/Contents/MacOS/Unity'
    assert mac_install.licensing_client_path == '/Applications/Unity/Hub/Editor/6000.1.7f1/Unity.app/Contents/Frameworks/UnityLicensingClient.app/Contents/MacOS/Unity.Licensing.Client'

    win_install = UnityInstall.parse('2022.3.55f1 (x64), installed at C:\\Program Files\\Unity\\Hub\\Editor\\2022.3.55f1\\Editor\\Unity.exe')
    assert win_install is not None
    assert win_install == UnityInstall(
        version=UnityVersion(
            major=2022,
            minor=3,
            patch=55,
            revision='f1',
        ),
        architecture='x64',
        path=os.path.normpath('C:/Program Files/Unity/Hub/Editor/2022.3.55f1/Editor/Unity.exe'),
    )
    assert win_install.editor_path == os.path.normpath('C:/Program Files/Unity/Hub/Editor/2022.3.55f1/Editor/Unity.exe')
    assert win_install.licensing_client_path == os.path.normpath('C:/Program Files/Unity/Hub/Editor/2022.3.55f1/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client.exe')

    linux_install = UnityInstall.parse('2023.2.0f1 (x64), installed at /home/username/Unity/Hub/Editor/2023.2.0f1/Editor/Unity')
    assert linux_install is not None
    assert linux_install == UnityInstall(
        version=UnityVersion(
            major=2023,
            minor=2,
            patch=0,
            revision='f1',
        ),
        architecture='x64',
        path='/home/username/Unity/Hub/Editor/2023.2.0f1/Editor/Unity'
    )
    assert linux_install.editor_path == '/home/username/Unity/Hub/Editor/2023.2.0f1/Editor/Unity'


def test_resolve_unity_install():
    unity_6000 = UnityInstall.parse('6000.1.7f1 (Apple silicon), installed at /Applications/Unity/Hub/Editor/6000.1.7f1/Unity.app')
    unity_2023 = UnityInstall.parse('2022.3.55f1 (Apple silicon), installed at /Applications/Unity/Hub/Editor/2022.3.55f1/Unity.app')
    assert unity_6000
    assert unity_2023
    installs = [unity_6000, unity_2023]

    assert resolve_unity_install(installs, '2022') == unity_2023
    assert resolve_unity_install(installs, '6000') == unity_6000
    assert resolve_unity_install(installs, '6000.1.7') == unity_6000
    assert resolve_unity_install(installs, '6000.2') is None
    assert resolve_unity_install(installs, '2021') is None


def test_match_unity_version():
    versions = [UnityVersion.parse(s) for s in [
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
    ]:
        got = match_unity_version(versions, version_prefix)
        assert got
        assert got == want
