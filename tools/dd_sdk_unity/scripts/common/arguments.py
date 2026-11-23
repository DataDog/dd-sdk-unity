# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

import argparse


def parse_test_filters(value: str) -> list[str]:
    filters = [part.strip() for part in value.split(',') if part.strip()]
    if not filters:
        raise argparse.ArgumentTypeError('Provide at least one test name or pattern')
    return filters
