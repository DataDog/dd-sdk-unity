// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration
{
    [PrebuildSetup(typeof(IntegrationTestEnvironment))]
    [PostBuildCleanup(typeof(IntegrationTestEnvironment))]
    public abstract class IntegrationTestBase
    {
    }
}
