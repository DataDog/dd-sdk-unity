# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""Run commands with captured output or the current terminal."""
import os
import signal
import subprocess

from dd_sdk_unity.scripts.common.log import get_default_logger


def run_cmd_streaming(*args: str) -> int:
    """Keep terminal detection and progress displays intact."""
    return subprocess.run(args, check=False, stderr=subprocess.STDOUT).returncode


def capture_output(*args: str) -> tuple[str, str]:
    result = subprocess.run(args, check=False, capture_output=True, text=True)
    if result.returncode:
        get_default_logger().error((result.stdout + result.stderr).rstrip())
        result.check_returncode()
    return result.stdout, result.stderr


def stop_process(process: subprocess.Popen[bytes]) -> None:
    if process.poll() is None:
        try:
            if os.name == 'nt':
                process.terminate()
            else:
                os.killpg(process.pid, signal.SIGTERM)
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            if os.name == 'nt':
                process.kill()
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait(timeout=5)
        except ProcessLookupError:
            process.wait(timeout=5)
