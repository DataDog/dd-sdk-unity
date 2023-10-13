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

        public InternalLogger()
        {
        }

        public void Log(DdLogLevel level, string message)
        {
            var unityLogLevel = DdLogHelpers.DdLogLevelToLogType(level);
            Debug.unityLogger.Log(unityLogLevel, DatadogTag, message);
        }
    }
}
