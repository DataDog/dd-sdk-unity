// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.
using System.Runtime.InteropServices;
using Datadog.Unity;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
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
            var options = DatadogConfigurationOptions.Load();
            if (options.Enabled)
            {
                DatadogSdk.InitWithPlatform(new DatadogiOSPlatform(), options);
            }
        }
    }

    internal class DatadogiOSPlatform : IDatadogPlatform
    {
        public void Init(DatadogConfigurationOptions options)
        {
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            Datadog_SetTrackingConsent((int)trackingConsent);
        }

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            var innerLogger = DatadogiOSLogger.Create(options);
            return new DdWorkerProxyLogger(worker, innerLogger);
        }

        public IDdRum InitRum(DatadogConfigurationOptions options)
        {
            return new DatadogiOSRum();
        }

        public void SendDebugTelemetry(string message)
        {
            Datadog_SendDebugTelemetry(message);
        }

        public void SendErrorTelemetry(string message, string stack, string kind)
        {
            Datadog_SendErrorTelemetry(message, stack, kind);
        }

        [DllImport("__Internal")]
        private static extern void Datadog_SetTrackingConsent(int trackingConsent);

        [DllImport("__Internal")]
        private static extern void Datadog_SendDebugTelemetry(string message);

        [DllImport("__Internal")]
        private static extern void Datadog_SendErrorTelemetry(string message, string stack, string kind);
    }
}
