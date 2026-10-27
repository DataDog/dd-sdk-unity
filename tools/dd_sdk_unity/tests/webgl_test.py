# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Chrome checks for the result bridge, compressed assets, and stalled players."""
import gzip
import json
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

import pytest
from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from dd_sdk_unity.scripts.common.log import init_logger
from dd_sdk_unity.scripts.common.xslt import transform_nunit_to_junit
from dd_sdk_unity.tests.webgl import run_webgl_tests

ROOT = Path(__file__).resolve().parents[3]
FIXTURES = ROOT / 'tools/dd_sdk_unity/scripts/common/apple/fixtures'
CHROME_AVAILABLE = Path('/Applications/Google Chrome.app').exists() or shutil.which('google-chrome') is not None

pytestmark = pytest.mark.skipif(not CHROME_AVAILABLE, reason='Google Chrome is not installed')


def _player(tmp_path, xml):
    player = tmp_path / 'player'
    player.mkdir()
    bridge = ROOT / 'packages/Datadog.Unity/Tests/Integration/IntegrationTestResult.jslib'
    (player / 'bridge.js').write_text(bridge.read_text())
    (player / 'empty.wasm.gz').write_bytes(gzip.compress(b'\0asm\1\0\0\0'))
    (player / 'index.html').write_text('''<!doctype html>
        <script>
            const LibraryManager = {library: {}};
            const mergeInto = Object.assign;
            const UTF8ToString = value => value;
        </script>
        <script src="bridge.js"></script>
        <script>
            WebAssembly.instantiateStreaming(fetch('empty.wasm.gz')).then(() => {
                console.log('WASM asset loaded');
                LibraryManager.library.DDTests_SetResult(''' + json.dumps(xml) + ''');
            });
        </script>''')
    return player


def test_chrome_bridge_preserves_passes_and_failures_and_loads_compressed_wasm(tmp_path):
    init_logger()
    player = _player(tmp_path, (FIXTURES / 'runtime-suite-given.xml').read_text())
    result, log = tmp_path / 'nunit.xml', tmp_path / 'browser.log'
    run_webgl_tests(player, result, log, timeout_ms=10_000)
    assert ET.canonicalize(from_file=result, strip_text=True) == ET.canonicalize(
        from_file=FIXTURES / 'runtime-suite-expected.xml', strip_text=True)
    junit = tmp_path / 'junit.xml'
    transform_nunit_to_junit(str(result), str(junit))
    assert len(ET.parse(junit).findall('.//testcase')) == 2
    assert len(ET.parse(junit).findall('.//failure')) == 1
    assert 'WASM asset loaded' in log.read_text()


def test_chrome_rejects_empty_results_and_saves_diagnostics(tmp_path):
    init_logger()
    player = _player(tmp_path, '<test-suite/>')
    with pytest.raises(RuntimeError, match='at least one test case'):
        run_webgl_tests(player, tmp_path / 'nunit.xml', tmp_path / 'browser.log', timeout_ms=10_000)
    assert not (tmp_path / 'nunit.xml').exists()
    assert (tmp_path / 'browser.png').is_file()
    assert (tmp_path / 'browser.zip').is_file()


def test_chrome_stalled_player_times_out_and_saves_diagnostics(tmp_path):
    init_logger()
    player = tmp_path / 'player'
    player.mkdir()
    (player / 'index.html').write_text('<!doctype html><title>Stalled player</title>')
    with pytest.raises(PlaywrightTimeoutError):
        run_webgl_tests(player, tmp_path / 'nunit.xml', tmp_path / 'browser.log', timeout_ms=100)
    assert not (tmp_path / 'nunit.xml').exists()
    assert (tmp_path / 'browser.png').is_file()
    assert (tmp_path / 'browser.zip').is_file()
