// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Datadog.Unity.Logs;
using Datadog.Unity.Worker;
using UnityEngine;

namespace Datadog.Unity
{
    public class DatadogSdk
    {
        public static readonly DatadogSdk Instance = new();

        private IDatadogPlatform _platform = new DatadogNoopPlatform();
        private DdUnityLogHandler _logHandler;
        private DatadogWorker _worker;

        private DatadogSdk()
        {
        }

        public IDdLogger DefaultLogger
        {
            get; private set;
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
            return _platform?.CreateLogger(options, _worker);
        }

        internal static void InitWithPlatform(IDatadogPlatform platform, DatadogConfigurationOptions options)
        {
            Instance.Init(platform, options);
        }

        private void Init(IDatadogPlatform platform, DatadogConfigurationOptions options)
        {
            _platform = platform;
            platform.Init(options);

            // Create our worker thread
            _worker = new();
            _worker.AddProcessor(DdLogsProcessor.LogsTargetName, new DdLogsProcessor());
            _worker.Start();

            var loggingOptions = new DatadogLoggingOptions();
            DefaultLogger = _platform.CreateLogger(loggingOptions, _worker);
            if (options.ForwardUnityLogs)
            {
                _logHandler = new(DefaultLogger);
                _logHandler.Attach();
            }

            Application.quitting += OnQuitting;
        }

        private void ShutdownInstance()
        {
            _platform = null;
            DefaultLogger = null;
            _logHandler?.Detach();
            _logHandler = null;
        }

        private void OnQuitting()
        {
            _worker.Stop();
        }
    }
}
