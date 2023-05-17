// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity.Logs;
using UnityEngine;

namespace Datadog.Unity.iOS
{
    public class DatadogiOSLogger : IDdLogger
    {
        private readonly string _loggerId;

        private DatadogiOSLogger(string loggerId)
        {
            _loggerId = loggerId;
        }

        public static DatadogiOSLogger Create(DatadogLoggingOptions options)
        {
            var jsonOptions = JsonUtility.ToJson(options, false);
            var loggerId = DatadogLoggingBridge.DatadogLogging_CreateLogger(jsonOptions);
            if (loggerId != null)
            {
                return new DatadogiOSLogger(loggerId);
            }

            return null;
        }

        public override void Log(DdLogLevel level, string message, Dictionary<string, object> attributes, Exception error = null)
        {
            // TODO: RUMM-3271, RUMM-3272 - Support attributes and errors
            DatadogLoggingBridge.DatadogLogging_Log(_loggerId, (int)level, message);
        }
    }

    internal static class DatadogLoggingBridge
    {
        [DllImport("__Internal")]
        public static extern string DatadogLogging_CreateLogger(string optionsJson);

        [DllImport("__Internal")]
        public static extern void DatadogLogging_Log(string loggerId, int logLevel, string message);
    }
}
