"""
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import pytest

import subprocess
from typing import List

from .shell import run_cmd, run_cmd_streaming


@pytest.mark.parametrize('merge_stderr', [False, True])
def test_run_cmd(merge_stderr: bool) -> None:
    got_stdout_lines: List[str] = []
    got_stderr_lines: List[str] = []
    def _handle_output(line: str, is_stderr: bool):
        if is_stderr:
            got_stderr_lines.append(line)
        else:
            got_stdout_lines.append(line)

    exitcode = run_cmd(
        '/bin/sh', '-c', 'echo "out1" && >&2 echo "err1" && echo "out2"',
        merge_stderr=merge_stderr,
        output_handler=_handle_output,
    )
    assert exitcode == 0
    assert got_stdout_lines == (['out1', 'err1', 'out2'] if merge_stderr else ['out1', 'out2'])
    assert got_stderr_lines == ([] if merge_stderr else ['err1'])


@pytest.mark.parametrize('run', [run_cmd, run_cmd_streaming])
def test_run_cmd_exitcode(run) -> None:
    assert run('/bin/sh', '-c', 'exit 42') == 42


def test_run_cmd_raise() -> None:
    with pytest.raises(subprocess.CalledProcessError):
        run_cmd('/bin/sh', '-c', 'exit 42', raise_on_nonzero_exitcode=True)
