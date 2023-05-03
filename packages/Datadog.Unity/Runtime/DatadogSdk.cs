// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using UnityEngine;

namespace Datadog.Unity
{
    public class DatadogSdk
    {
        public static readonly DatadogSdk Instance = new ();

        private IDatadogPlatform _platform;

        private DatadogSdk()
        {
        }

        public static void InitWithPlatform(IDatadogPlatform platform)
        {
            Instance._platform = platform;
        }

        public IDdLogger CreateLogger()
        {
            return _platform?.CreateLogger();
        }
    }
}
