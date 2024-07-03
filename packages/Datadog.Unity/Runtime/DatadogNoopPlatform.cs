// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using UnityEngine;

namespace Datadog.Unity
{
    internal class DatadogNoOpPlatform : IDatadogPlatform
    {
        public DatadogNoOpPlatform()
        {
            if (Application.isEditor)
            {
                Debug.LogWarning("Datadog SDK is running in Editor mode. No data will be sent to Datadog.");
            }
        }

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {
        }

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            return new DdNoOpLogger();
        }

        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
        }

        public void RemoveLogsAttribute(string key)
        {
        }

        public void SetUserInfo(string id, string name, string email, Dictionary<string, object> extraInfo)
        {
        }

        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
        }

        public IDdRum InitRum(DatadogConfigurationOptions options)
        {
            return new DdNoOpRum();
        }

        public void SendDebugTelemetry(string message)
        {
        }

        public void SendErrorTelemetry(string message, string stack, string kind)
        {
        }

        public void Init(DatadogConfigurationOptions options)
        {
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
        }

        public void ClearAllData()
        {
        }
    }
}
