// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Logs;

namespace Datadog.Unity
{
    // These are mappings to android.util.Log
    internal enum AndroidLogLevel
    {
        Verbose = 2,
        Debug = 3,
        Info = 4,
        Warn = 5,
        Error = 6,
        Assert = 7,
    }

    internal static class InternalHelpers
    {
        public static void Wrap(string functionName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                var internalLogger = DatadogSdk.Instance.InternalLogger;
                internalLogger.Log(DdLogLevel.Warn, $"There was an error calling {functionName}: {e}");
                internalLogger.Log(DdLogLevel.Warn, "This is likely an error in the DatadogSdk. Please report it to Datadog.");
                internalLogger.TelemetryError($"Error in {functionName}", e);
            }
        }

        internal static AndroidLogLevel DdLogLevelToAndroidLogLevel(DdLogLevel logLevel)
        {
            return logLevel switch
            {
                DdLogLevel.Debug => AndroidLogLevel.Debug,
                DdLogLevel.Info => AndroidLogLevel.Info,
                DdLogLevel.Notice => AndroidLogLevel.Info,
                DdLogLevel.Warn => AndroidLogLevel.Warn,
                DdLogLevel.Error => AndroidLogLevel.Error,
                DdLogLevel.Critical => AndroidLogLevel.Assert,
                _ => AndroidLogLevel.Debug,
            };
        }
    }

    internal static class DictionaryHelpers
    {
        public static void Copy<K, V>(this Dictionary<K, V> self, Dictionary<K, V> other)
        {
            foreach (var kvp in other)
            {
                self[kvp.Key] = kvp.Value;
            }
        }
    }
}
