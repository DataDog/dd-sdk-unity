// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.
using Datadog.Unity;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: UnityEngine.Scripting.Preserve]
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Datadog.Unity.iOS
{
    [Preserve]
    public static class DatadogInitialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void InitializeDatadog()
        {
            DatadogSdk.InitWithPlatform(new DatadogiOSPlatform());
        }
    }

    internal class DatadogiOSPlatform : IDatadogPlatform
    {
        public void Init(DatadogConfigurationOptions options)
        {
        }

        public IDdLogger CreateLogger()
        {
            return DatadogiOSLogger.Create();
        }
    }
}
