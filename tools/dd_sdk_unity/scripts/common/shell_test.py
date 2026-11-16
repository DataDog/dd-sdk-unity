# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

import subprocess

import pytest

from dd_sdk_unity.scripts.common.shell import capture_output, run_cmd_streaming


def test_capture_output_keeps_streams_separate():
    assert capture_output('/bin/sh', '-c', 'echo out; echo err >&2') == ('out\n', 'err\n')


def test_capture_output_preserves_failure_details():
    with pytest.raises(subprocess.CalledProcessError) as failure:
        capture_output('/bin/sh', '-c', 'echo out; echo err >&2; exit 42')
    assert failure.value.returncode == 42
    assert failure.value.output == 'out\n'
    assert failure.value.stderr == 'err\n'


def test_streaming_command_inherits_output_and_returns_exitcode(capfd):
    assert run_cmd_streaming('/bin/sh', '-c', 'echo out; echo err >&2; exit 42') == 42
    assert capfd.readouterr().out == 'out\nerr\n'
