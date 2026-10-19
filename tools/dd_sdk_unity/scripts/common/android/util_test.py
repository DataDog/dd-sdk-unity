# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

from pathlib import Path

import pytest

from dd_sdk_unity.scripts.common.android import util
from dd_sdk_unity.scripts.common.android.adb import Adb
from dd_sdk_unity.scripts.common.android.avdmanager import AvdManager
from dd_sdk_unity.scripts.common.android.emulator import AndroidEmulator
from dd_sdk_unity.scripts.common.android.sdkmanager import AndroidSdkManager


def test_all_tools_use_the_selected_sdk(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    sdk = tmp_path / 'sdk'
    paths = [
        'platform-tools/adb',
        'cmdline-tools/latest/bin/avdmanager',
        'cmdline-tools/latest/bin/sdkmanager',
        'emulator/emulator',
    ]
    for path in paths:
        binary = sdk / path
        binary.parent.mkdir(parents=True, exist_ok=True)
        binary.touch()
    monkeypatch.setenv('ANDROID_HOME', str(sdk))
    monkeypatch.setattr(util.platform, 'system', lambda: 'Linux')

    tools = [Adb.require(), AvdManager.require(), AndroidSdkManager.require(), AndroidEmulator.require()]
    assert [tool.path for tool in tools] == [str(sdk / path) for path in paths]


def test_android_home_is_required(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv('ANDROID_HOME', raising=False)

    with pytest.raises(RuntimeError, match='ANDROID_HOME is not set'):
        Adb.require()
