// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Runtime.InteropServices;

namespace Datadog.Unity.iOS
{
    public class DatadogiOSLogger : IDdLogger
    {
        private readonly string _loggerId;

        private DatadogiOSLogger(string loggerId)
        {
            _loggerId = loggerId;
        }

        public static DatadogiOSLogger Create()
        {
            var loggerId = DatadogLoggingBridge.DatadogLogging_CreateLog();
            if (loggerId != null)
            {
                return new DatadogiOSLogger(loggerId);
            }

            return null;
        }

        public void Log(string message)
        {
            DatadogLoggingBridge.DatadogLogging_Log(_loggerId, message);
        }
    }

    internal static class DatadogLoggingBridge
    {
        [DllImport("__Internal")]
        public static extern string DatadogLogging_CreateLog();

        [DllImport("__Internal")]
        public static extern void DatadogLogging_Log(string loggerId, string message);
    }
}
