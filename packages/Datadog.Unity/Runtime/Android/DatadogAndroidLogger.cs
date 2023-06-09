// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Logs;
using UnityEngine;

namespace Datadog.Unity.Android
{
    internal class DatadogAndroidLogger : IDdLogger
    {
        private readonly AndroidJavaObject _androidLogger;

        public DatadogAndroidLogger(AndroidJavaObject androidLogger)
        {
            _androidLogger = androidLogger;
        }

        public override void AddTag(string tag, string value = null)
        {
            if (value != null) {
                _androidLogger.Call("addTag", tag, value);
            } else {
                _androidLogger.Call("addTag", tag);
            }
        }

        public override void Log(DdLogLevel level, string message, Dictionary<string, object> attributes, Exception error = null)
        {
            // TODO: RUMM-3271, RUMM-3272 - Support attributes and errors
            var androidLevel = DatadogConfigurationHelpers.DdLogLevelToAndroidLogLevel(level);
            _androidLogger.Call("log", (int)androidLevel, message);
        }
    }
}
