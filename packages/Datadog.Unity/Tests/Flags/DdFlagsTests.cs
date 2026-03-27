// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class DdFlagsTests
    {
        [TearDown]
        public void TearDown()
        {
            DdFlags.Shutdown();
        }

        [Test]
        public void Enable_ThrowsWhenEnvNotConfigured()
        {
            // DatadogSettings.asset has Env left blank (the default for new projects).
            // DdFlags.Enable() must throw rather than silently sending a request that
            // the edge layer rejects with 400 ("dd_env cannot be empty").
            Assert.Throws<InvalidOperationException>(() => DdFlags.Enable());
        }
    }
}
