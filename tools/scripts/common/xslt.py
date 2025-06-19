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
