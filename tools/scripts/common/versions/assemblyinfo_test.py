import pytest

import io

from .assemblyinfo import _modify_assemblyinfo_impl
from .semver import Version


__old_assemblyinfo_cs__ = '''// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyVersion("1.0.0")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.tests")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.android")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.ios")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.webgl")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.Editor")]

// This is the Moq library
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
'''

__new_version__ = Version.parse('1.5.1')

__new_assemblyinfo_cs__ = '''// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyVersion("1.5.1")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.tests")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.android")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.ios")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.webgl")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.Editor")]

// This is the Moq library
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
'''


def test_modify_assemblyinfo_impl():
    infile = io.StringIO(__old_assemblyinfo_cs__)
    got = _modify_assemblyinfo_impl(infile, __new_version__)
    assert got == __new_assemblyinfo_cs__
