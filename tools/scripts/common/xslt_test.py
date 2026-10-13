"""Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

Given/expected checks for NUnit-to-JUnit XML conversion.
"""
from pathlib import Path
import xml.etree.ElementTree as ET

import pytest

from .xslt import transform_nunit_to_junit


FIXTURES = Path(__file__).with_name('fixtures') / 'nunit-junit'


@pytest.mark.parametrize('given', sorted(FIXTURES.glob('*-given.xml')), ids=lambda path: path.stem.removesuffix('-given'))
def test_nunit_conversion_matches_expected_junit(tmp_path, given):
    junit = tmp_path / 'junit.xml'
    transform_nunit_to_junit(str(given), str(junit))
    expected = given.with_name(given.name.replace('-given.xml', '-expected.xml'))
    assert ET.canonicalize(from_file=junit, strip_text=True) == ET.canonicalize(from_file=expected, strip_text=True)
