# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.

import io

import pytest

from dd_sdk_unity.release.assemblyinfo import _modify_assemblyinfo_impl
from dd_sdk_unity.scripts.common.versions.semver import Version

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

__expected_assemblyinfo_cs__ = '''// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyVersion("{version.core}")]
[assembly: AssemblyInformationalVersion("{version}")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.tests")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.android")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.ios")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.webgl")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.Editor")]

// This is the Moq library
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
'''


@pytest.mark.parametrize("version", ["2.0.0-preview1", "2.0.0-preview.1+build.5", "2.0.0"])
def test_modify_assemblyinfo_sets_version_attributes(version):
    assemblyinfo = io.StringIO(__old_assemblyinfo_cs__)
    version = Version.parse(version)
    result = _modify_assemblyinfo_impl(assemblyinfo, version)
    assert result == __expected_assemblyinfo_cs__.format(version=version)
