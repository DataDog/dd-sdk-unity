# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Report compiler output blocks from a failed Unity invocation."""
from pathlib import Path

from dd_sdk_unity.scripts.common.log import get_default_logger


def report_unity_failure(log_path: str | Path) -> None:
    log = get_default_logger()
    path = Path(log_path).resolve()
    found_output = False
    if path.is_file():
        with path.open(errors='replace') as source:
            in_output = False
            for line in source:
                if line.rstrip() == '## Output:':
                    in_output = True
                elif in_output:
                    if not line.strip():
                        in_output = False
                    else:
                        log.error(line.rstrip())
                        found_output = True
        log.info(f'Full Unity log at {path}')

    launcher_log = path.with_name('launcher.log')
    if not found_output and launcher_log.is_file():
        log.info(f'Launcher log at {launcher_log}')
