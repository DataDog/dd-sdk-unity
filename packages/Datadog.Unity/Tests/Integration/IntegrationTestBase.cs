// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration
{
    // All integration tests should inherit this class to apply shared SDK settings before
    // the player build and restore the original settings afterward.
#if UNITY_EDITOR
    [PrebuildSetup(typeof(IntegrationTestEnvironment))]
    [PostBuildCleanup(typeof(IntegrationTestEnvironment))]
#endif
    public abstract class IntegrationTestBase
    {
    }
}
