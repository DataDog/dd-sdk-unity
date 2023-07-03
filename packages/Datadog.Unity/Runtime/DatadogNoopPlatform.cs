// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Datadog.Unity.Logs;

namespace Datadog.Unity
{
    internal class DatadogNoopPlatform : IDatadogPlatform
    {
        public IDdLogger CreateLogger(DatadogLoggingOptions options)
        {
            return new DdNoopLogger();
        }

        public void Init(DatadogConfigurationOptions options)
        {
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
        }
    }
}
