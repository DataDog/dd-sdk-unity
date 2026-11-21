# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for invoking xcodebuild, which builds Xcode projects, and xcbeautify,
which formats the output from xcodebuild in a more human-readable way.
"""
import shlex
import subprocess
from contextlib import nullcontext
from pathlib import Path

from dd_sdk_unity.scripts.common.log import get_default_logger
from dd_sdk_unity.scripts.common.shell import stop_process


def run_xcodebuild(cwd: str, args: list[str], *, log_path: str | Path | None = None) -> None:
    command = ['xcodebuild', *args]
    pipeline = shlex.join(command) + ' 2>&1 | xcbeautify'
    log = get_default_logger()
    path = Path(log_path).resolve() if log_path is not None else None
    if path is not None:
        path.parent.mkdir(parents=True, exist_ok=True)
        log.info(f'Starting Xcode build, log: {path}')
    else:
        log.info('Running: ' + pipeline)
    with path.open('w') if path is not None else nullcontext(None) as output:
        if output is not None:
            output.write('$ ' + shlex.join(command) + '\n')
            output.flush()
        process = subprocess.Popen(command if path is not None else
                                   ['bash', '-o', 'pipefail', '-c', pipeline], cwd=cwd,
                                   stdout=output, stderr=subprocess.STDOUT,
                                   stdin=subprocess.DEVNULL, start_new_session=True)
        try:
            result = process.wait()
            if result:
                if path is not None:
                    log.error(f'Xcode build failed (exit code {result}). Full build log at {path}')
                raise subprocess.CalledProcessError(result, command)
        finally:
            stop_process(process)
    if path is not None:
        log.info('Finished Xcode build')
