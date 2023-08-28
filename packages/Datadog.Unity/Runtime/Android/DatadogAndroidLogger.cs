// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Logs;
using Newtonsoft.Json;
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

        public override void AddAttribute(string key, object value)
        {
            _androidLogger.Call("addAttribute", key, value);
        }

        public override void AddTag(string tag, string value = null)
        {
            if (value != null)
            {
                var javaValue = DatadogAndroidHelpers.ObjectToJavaObject(value);
                _androidLogger.Call("addTag", tag, javaValue);
            }
            else
            {
                _androidLogger.Call("addTag", tag);
            }
        }

        public override void Log(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            var androidLevel = DatadogConfigurationHelpers.DdLogLevelToAndroidLogLevel(level);

            using var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);
            var errorKind = error?.GetType()?.ToString();
            var errorMessage = error?.Message;
            var errorStack = error?.StackTrace?.ToString();

            _androidLogger.Call("log", (int)androidLevel, message, errorKind, errorMessage, errorStack, javaAttributes);
        }

        public override void RemoveAttribute(string key)
        {
            _androidLogger.Call("removeAttribute", key);
        }

        public override void RemoveTag(string tag)
        {
            _androidLogger.Call("removeTag", tag);
        }

        public override void RemoveTagsWithKey(string key)
        {
            _androidLogger.Call("removeTagsWithKey", key);
        }
    }
}
