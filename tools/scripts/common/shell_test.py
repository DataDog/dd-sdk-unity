import pytest

import subprocess
from typing import List

from .shell import run_cmd


def test_run_cmd():
    got_stdout_lines: List[str] = []
    got_stderr_lines: List[str] = []
    def _handle_output(line: str, is_stderr: bool):
        if is_stderr:
            got_stderr_lines.append(line)
        else:
            got_stdout_lines.append(line)

    exitcode = run_cmd('/bin/sh', '-c', 'echo "out1" && >&2 echo "err1" && echo "out2"', output_handler=_handle_output)
    assert exitcode == 0
    assert got_stdout_lines == ['out1', 'out2']
    assert got_stderr_lines == ['err1']


def test_run_cmd_exitcode():
    assert run_cmd('/bin/sh', '-c', 'exit 42') == 42


def test_run_cmd_raise():
    with pytest.raises(subprocess.CalledProcessError):
        run_cmd('/bin/sh', '-c', 'exit 42', raise_on_nonzero_exitcode=True)
