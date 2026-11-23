# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Print a concise test verdict with assertion failures from JUnit results."""
from pathlib import Path

from junitparser.junitparser import Error, Failure, JUnitXml, TestCase

from dd_sdk_unity.scripts.common.log import get_default_logger


def report_test_results(junit_path: str, exitcode: int) -> bool:
    """Report JUnit results and return whether the test run failed."""
    if not Path(junit_path).is_file():
        raise RuntimeError(f'Test runner failed to write test results to {junit_path}')
    results = JUnitXml.fromfile(junit_path)

    passed = 0
    skipped = 0
    failures: list[TestCase] = []
    for suite in results:
        for case in suite:
            if case.is_skipped:
                skipped += 1
            elif case.is_passed:
                passed += 1
            else:
                failures.append(case)

    log = get_default_logger()
    if failures:
        total = passed + skipped + len(failures)
        log.error(f'Test finished: {len(failures)} of {total} failed ({skipped} skipped)')
        for case in failures:
            log.error(f'❌ {case.name}')
            for result in case.result:
                if isinstance(result, (Failure, Error)):
                    message = result.message or result.text
                    if message:
                        log.error(message.rstrip())
    elif exitcode:
        log.error(f'Test run failed (exit code {exitcode}); results report '
                  f'{passed} passed, {skipped} skipped')
    else:
        log.info(f'Test finished: {passed} passed, {skipped} skipped')
    log.info(f'Full test results at {Path(junit_path).resolve()}')
    return bool(failures) or exitcode != 0
