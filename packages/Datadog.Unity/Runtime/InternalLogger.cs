// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Logs;
using Datadog.Unity.Worker;
using UnityEngine;
using UnityEngine.Pool;

namespace Datadog.Unity.Core
{
    public interface IInternalLogger
    {
        public const string DatadogTag = "Datadog";

        public void Log(DdLogLevel level, string message);
        public void TelemetryError(string message, Exception exception);
        public void TelemetryDebug(string message);
    }

    /// <summary>
    /// InternalLogger is used to log messages to users of the DatadogSdk, bypassing sending logs
    /// to Datadog. It is also used for sending telemetry to Datadog about the performance of
    /// the SDK.
    /// </summary>
    internal class InternalLogger : IInternalLogger
    {
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
            Debug.unityLogger.Log(unityLogLevel, IInternalLogger.DatadogTag, message);
        }

        public void TelemetryError(string message, Exception exception)
        {
            _worker.AddMessage(DdTelemetryProcessor.TelemetryErrorMessage.Create(
                message,
                exception?.StackTrace?.ToString(),
                exception?.GetType().ToString()));
        }

        public void TelemetryDebug(string message)
        {
            _worker.AddMessage(DdTelemetryProcessor.TelemetryDebugMessage.Create(
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
            private static ObjectPool<TelemetryDebugMessage> _pool = new (
                createFunc: () => new TelemetryDebugMessage(), actionOnRelease: (obj) => obj.Reset());

            private TelemetryDebugMessage()
            {
            }

            public string FeatureTarget => DdTelemetryProcessor.TelemetryTargetName;

            public string Message { get; private set; }

            public static TelemetryDebugMessage Create(string message)
            {
                var obj = _pool.Get();

                obj.Message = message;

                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Message = null;
            }
        }

        internal class TelemetryErrorMessage : IDatadogWorkerMessage
        {
            private static ObjectPool<TelemetryErrorMessage> _pool = new (
                createFunc: () => new TelemetryErrorMessage(), actionOnRelease: (obj) => obj.Reset());

            private TelemetryErrorMessage()
            {
            }

            public string FeatureTarget => DdTelemetryProcessor.TelemetryTargetName;

            public string Message { get; private set; }

            public string Stack { get; private set; }

            public string Kind { get; private set; }

            public static TelemetryErrorMessage Create(string message, string stack, string kind)
            {
                var obj = _pool.Get();
                obj.Message = message;
                obj.Stack = stack;
                obj.Kind = kind;
                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Message = null;
                Stack = null;
                Kind = null;
            }
        }
    }
}
