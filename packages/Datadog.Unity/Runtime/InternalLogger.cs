// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Logs;
using Datadog.Unity.Worker;
using JetBrains.Annotations;
using UnityEngine;

namespace Datadog.Unity.Core
{
    /// <summary>
    /// InternalLogger is used to log messages to users of the DatadogSdk, bypassing sending logs
    /// to Datadog. It is also used for sending telemetry to Datadog about the performance of
    /// the SDK.
    /// </summary>
    internal class InternalLogger
    {
        public const string DatadogTag = "Datadog";

        private DatadogWorker _worker;

        public InternalLogger(DatadogWorker worker, IDatadogPlatform platform)
        {
            _worker = worker;

            var processor = new DdTelemetryProcessor(platform);
            _worker.AddProcessor(DdTelemetryProcessor.TelemetryTargetName, processor);
        }

        public void Log(DdLogLevel level, string message)
        {
            var unityLogLevel = DdLogHelpers.DdLogLevelToLogType(level);
            Debug.unityLogger.Log(unityLogLevel, DatadogTag, message);
        }

        public void TelemetryError(string message, Exception exception)
        {
            _worker.AddMessage(new DdTelemetryProcessor.TelemetryErrorMessage(
                message,
                exception?.StackTrace?.ToString(),
                exception?.GetType().ToString()));
        }

        public void TelemetryDebug(string message)
        {
            _worker.AddMessage(new DdTelemetryProcessor.TelemetryDebugMessage(
                message));
        }
    }

    internal class DdTelemetryProcessor : IDatadogWorkerProcessor
    {
        public static readonly string TelemetryTargetName = "telemetry";

        private IDatadogPlatform _platform;

        public DdTelemetryProcessor(IDatadogPlatform platform)
        {
            _platform = platform;
        }

        public void Process(IDatadogWorkerMessage message)
        {
            switch (message)
            {
                case TelemetryDebugMessage msg:
                    _platform.SendDebugTelemetry(msg.Message);
                    break;
                case TelemetryErrorMessage msg:
                    _platform.SendErrorTelemetry(msg.Message, msg.Stack, msg.Kind);
                    break;
            }
        }

        internal class TelemetryDebugMessage : IDatadogWorkerMessage
        {
            public string FeatureTarget => DdTelemetryProcessor.TelemetryTargetName;

            public string Message { get; private set; }

            public TelemetryDebugMessage(string message)
            {
                Message = message;
            }
        }

        internal class TelemetryErrorMessage : IDatadogWorkerMessage
        {
            public string FeatureTarget => DdTelemetryProcessor.TelemetryTargetName;

            public string Message { get; private set; }

            public string Stack { get; private set; }

            public string Kind { get; private set; }

            public TelemetryErrorMessage(string message, string stack, string kind)
            {
                Message = message;
                Stack = stack;
                Kind = kind;
            }
        }
    }
}
