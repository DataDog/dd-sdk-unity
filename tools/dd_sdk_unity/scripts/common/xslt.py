"""
Utility code for converting between different XML test result formats.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os

from saxonche import PySaxonProcessor # type: ignore


__nunit3_junit_xslt__ = os.path.join(os.path.dirname(__file__), 'nunit3-junit.xslt')


def transform_nunit_to_junit(nunit_file: str, junit_file: str):
    with PySaxonProcessor(license=False) as proc:
        xsltproc = proc.new_xslt30_processor()
        xsltproc.transform_to_file(
            source_file=nunit_file,
            stylesheet_file=__nunit3_junit_xslt__, 
            output_file=junit_file,
        )
