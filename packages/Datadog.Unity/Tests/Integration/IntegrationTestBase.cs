// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.Threading.Tasks;
using NUnit.Framework;

#if UNITY_EDITOR
using Datadog.Unity.Tests.Editor;
#endif
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration
{
    // All integration tests should inherit this class to prepare devices and SDK settings
    // before the player build and restore the original settings afterward.
#if UNITY_EDITOR
    [PrebuildSetup(typeof(IntegrationTestSetup))]
    [PostBuildCleanup(typeof(IntegrationTestSetup))]
#endif
    public abstract class IntegrationTestBase
    {
        protected MockServerHelper MockServer { get; private set; }

        protected virtual bool ResetMockServerBeforeTest => true;

        [SetUp]
        public async Task PrepareMockServer()
        {
            MockServer = new MockServerHelper();
            if (ResetMockServerBeforeTest)
            {
                await MockServer.Reset();
            }
        }
    }
}
