// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

namespace Datadog.Unity
{
    public class DatadogSdk
    {
        public static DatadogSdk instance = new DatadogSdk();

        IDatadogPlatform platform;

        private DatadogSdk()
        {

        }

        public static void InitWithPlatform(IDatadogPlatform platform)
        {
            instance.platform = platform;
        }

        public IDdLogger CreateLogger()
        {
            return platform?.CreateLogger();
        }
    }
}
