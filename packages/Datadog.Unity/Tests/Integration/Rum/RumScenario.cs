// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

namespace Datadog.Unity.Tests.Integration.Rum
{
    internal static class RumScenario
    {
        public static void CloseView(string viewKey)
        {
#if UNITY_WEBGL
            // Browser views close when a successor starts; exclude it from scenario visit counts.
            DatadogSdk.Instance.Rum.StartView($"InitTestScene-{viewKey}-complete");
#else
            DatadogSdk.Instance.Rum.StopView(viewKey);
#endif
        }
    }
}
