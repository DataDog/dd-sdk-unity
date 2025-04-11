// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Datadog.Unity.Core;
using UnityEngine;

namespace Datadog.Unity.Worker
{
    internal interface IDatadogWorkerProcessor
    {
        public void Process(IDatadogWorkerMessage message);
    }

    internal interface IDatadogWorkerMessage
    {
        public string FeatureTarget { get; }

        public void Discard();
    }

    // In order to ensure that we don't interrupt the main game loop when communicating to
    // the PlatformSDKs, we use a secondary thread to send the data to the platform.
    internal abstract class DatadogWorker
    {
        private readonly Dictionary<string, IDatadogWorkerProcessor> _processors = new();

        public DatadogWorker()
        {
        }

        public abstract void Start();
        public abstract void Stop();
        public abstract void AddMessage(IDatadogWorkerMessage message);

        public void AddProcessor(string feature, IDatadogWorkerProcessor processor)
        {
            if (_processors.ContainsKey(feature))
            {
                // Don't replace processors
                return;
            }

            _processors[feature] = processor;
        }

        protected void HandleMessage(IDatadogWorkerMessage message)
        {
            if (_processors.ContainsKey(message.FeatureTarget))
            {
                var processor = _processors[message.FeatureTarget];
                try
                {
                    processor?.Process(message);
                }
                catch (Exception e)
                {
                    if (message.FeatureTarget != DdTelemetryProcessor.TelemetryTargetName)
                    {
                        // Don't get stuck repeatedly trying to report errors to telemetry
                        DatadogSdk.Instance.InternalLogger.TelemetryError(
                            $"Error processing message: {message}", e);
                    }
                }
            }
            else
            {
                DatadogSdk.Instance.InternalLogger.TelemetryError(
                    $"Attempting to send message to unknown feature: {message.FeatureTarget}", null);
            }

            message.Discard();
        }
    }
}
