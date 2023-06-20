// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Datadog.Unity.Logs;
using UnityEngine;

namespace Datadog.Unity
{
    public class DatadogSdk
    {
        public static readonly DatadogSdk Instance = new();

        private IDatadogPlatform _platform;

        private DatadogSdk()
        {
        }

        public IDdLogger DefaultLogger
        {
            get; private set;
        }

        public static void InitWithPlatform(IDatadogPlatform platform, DatadogConfigurationOptions options)
        {
            Instance.Init(platform, options);
        }

        public static void Shutdown()
        {
            Instance.ShutdownInstance();
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            Instance._platform.SetTrackingConsent(trackingConsent);
        }

        public IDdLogger CreateLogger(DatadogLoggingOptions options)
        {
            return _platform?.CreateLogger(options);
        }

        private void Init(IDatadogPlatform platform, DatadogConfigurationOptions options)
        {
            _platform = platform;

            var loggingOptions = new DatadogLoggingOptions();
            Instance.DefaultLogger = Instance._platform.CreateLogger(loggingOptions);

            if (options.ForwardUnityLogs)
            {
                Application.logMessageReceivedThreaded += OnLogMessageRecieved;
            }
        }

        private void ShutdownInstance()
        {
            _platform = null;
            DefaultLogger = null;
            Application.logMessageReceivedThreaded -= OnLogMessageRecieved;
        }

        private void OnLogMessageRecieved(string message, string stackTrace, LogType type)
        {
            var logLevel = DdLogHelpers.LogTypeToDdLogLevel(type);
            DefaultLogger?.Log(logLevel, message);
        }
    }
}
