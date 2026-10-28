# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

"""
Utility code for converting between different XML test result formats.
"""
import os
import xml.etree.ElementTree as ET
from pathlib import Path

from saxonche import PySaxonProcessor  # type: ignore

__nunit3_junit_xslt__ = os.path.join(os.path.dirname(__file__), 'nunit3-junit.xslt')


def collect_results(source: str | Path, destination: str | Path) -> None:
    """Wrap Unity's runtime suite in a NUnit test-run root for JUnit conversion."""
    root = ET.parse(source).getroot()
    if root.tag == 'test-suite':
        run = ET.Element('test-run', dict(root.attrib))
        run.append(root)
        root = run
    if root.tag != 'test-run' or not root.findall('.//test-case'):
        raise RuntimeError('Expected a NUnit test-run document containing at least one test case')
    ET.ElementTree(root).write(destination, encoding='utf-8', xml_declaration=True)


def transform_nunit_to_junit(nunit_file: str, junit_file: str) -> None:
    with PySaxonProcessor(license=False) as proc:
        xsltproc = proc.new_xslt30_processor()
        xsltproc.transform_to_file(
            source_file=nunit_file,
            stylesheet_file=__nunit3_junit_xslt__, 
            output_file=junit_file,
        )
