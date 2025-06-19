import pytest

import os
import tempfile

from .xslt import transform_nunit_to_junit


__test_nunit_xml__ = os.path.join(os.path.dirname(__file__), 'xslt_test_nunit.xml')
__test_junit_xml__ = os.path.join(os.path.dirname(__file__), 'xslt_test_junit.xml')


def test_transform_nunit_to_junit():
    with open(__test_junit_xml__, 'rb') as fp:
        want = fp.read()

    with tempfile.NamedTemporaryFile() as tmp:
        transform_nunit_to_junit(__test_nunit_xml__, tmp.name)
        tmp.seek(0)
        got = tmp.read()

    assert got == want
