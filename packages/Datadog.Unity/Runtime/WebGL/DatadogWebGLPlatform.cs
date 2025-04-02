// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: UnityEngine.Scripting.Preserve]
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Datadog.Unity.WebGL
{
    [Preserve]
    public static class DatadogInitialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void InitializeDatadog()
        {
            var options = DatadogConfigurationOptions.Load();
            if (options.Enabled)
            {
                var platform = new DatadogWebGLPlatform();
                platform.Init(options);
                DatadogSdk.InitWithPlatform(platform, options);
            }
        }
    }

    internal class DatadogWebGLPlatform : IDatadogPlatform
    {
        private DatadogWebGLLogs _logs = new DatadogWebGLLogs();

        public void Init(DatadogConfigurationOptions options)
        {
            _logs.Init(options);
        }

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {

        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
        }

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            var innerLogger = _logs.CreateLogger(options);
            return innerLogger;
            //return new DdWorkerProxyLogger(worker, innerLogger);
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

        public void ClearAllData()
        {

        }

        public string GetNativeStack(Exception error)
        {
            return string.Empty;
        }
    }
}
