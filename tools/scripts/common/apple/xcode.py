"""
Utility code for invoking xcodebuild, which builds Xcode projects, and xcbeautify,
which formats the output from xcodebuild in a more human-readable way.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import shlex
import subprocess
from typing import List

from common.log import get_default_logger
from common.shell import stop_process


def run_xcodebuild(cwd: str, args: List[str]):
    command = ['xcodebuild', *args]
    pipeline = shlex.join(command) + ' 2>&1 | xcbeautify'
    log = get_default_logger()
    log.info('Running: ' + pipeline)
    process = subprocess.Popen(['bash', '-o', 'pipefail', '-c', pipeline], cwd=cwd,
                               stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                               start_new_session=True)
    try:
        result = process.wait()
        if result:
            raise subprocess.CalledProcessError(result, command)
    finally:
        stop_process(process)
