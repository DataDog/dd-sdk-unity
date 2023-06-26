// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using UnityEngine;

namespace Datadog.Unity.Logs
{
    // Note - these levels should match the ordering of the Datadog.LogLevel enum in iOS
    public enum DdLogLevel
    {
        Debug = 0,
        Info = 1,
        Notice = 2,
        Warn = 3,
        Error = 4,
        Critical = 5,
    }

    internal static class DdLogHelpers
    {
        internal static DdLogLevel LogTypeToDdLogLevel(LogType logType)
        {
            return logType switch
            {
                LogType.Error => DdLogLevel.Error,
                LogType.Assert => DdLogLevel.Critical,
                LogType.Warning => DdLogLevel.Warn,
                LogType.Log => DdLogLevel.Info,
                LogType.Exception => DdLogLevel.Critical,
                _ => DdLogLevel.Info,
            };
        }
    }
}
