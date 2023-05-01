// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using UnityEngine;

namespace Datadog.Unity.Android
{
    internal class DatadogAndroidLogger : IDdLogger
    {
        private AndroidJavaObject _androidLogger;

        public DatadogAndroidLogger(AndroidJavaObject androidLogger)
        {
            _androidLogger = androidLogger;
        }

        public void Log(string message)
        {
            _androidLogger.Call("log", 4, message);
        }
    }
}
