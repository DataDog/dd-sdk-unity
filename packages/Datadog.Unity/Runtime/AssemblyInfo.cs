// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Reflection;
using System.Runtime.CompilerServices;

[assembly: AssemblyVersion("1.1.1")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.tests")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.android")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.ios")]
[assembly: InternalsVisibleTo("com.datadoghq.unity.Editor")]

// This is the Moq library
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
