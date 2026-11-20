// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: Preserve]
[assembly: AlwaysLinkAssembly]

namespace Datadog.Unity.Desktop
{
    [Preserve]
    public static class DatadogInitialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void InitializeDatadog()
        {
            var options = DatadogConfigurationOptions.Load();
            if (options == null || !options.Enabled)
            {
                return;
            }

            var platform = new DatadogDesktopPlatform();
            try
            {
                platform.Init(options);
                DatadogSdk.InitWithPlatform(platform, options);
                platform.Start();
            }
            catch (Exception e)
            {
                DatadogSdk.Shutdown();
                platform.Dispose();
                Debug.LogError($"Failed to initialize the Datadog desktop SDK. A no-op implementation will be used: {e}");
                DatadogSdk.InitWithPlatform(new DatadogNoOpPlatform(), options);
            }
        }
    }
}
