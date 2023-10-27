// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity.Logs;
using Newtonsoft.Json;
using UnityEngine;

namespace Datadog.Unity.iOS
{
    public class DatadogiOSLogger : DdLogger
    {
        private readonly string _loggerId;

        private DatadogiOSLogger(DdLogLevel logLevel, float sampleRate, string loggerId)
            : base(logLevel, sampleRate)
        {
            _loggerId = loggerId;
        }

        public static DatadogiOSLogger Create(DatadogLoggingOptions options)
        {
            var jsonOptions = JsonUtility.ToJson(options, false);
            var loggerId = DatadogLoggingBridge.DatadogLogging_CreateLogger(jsonOptions);
            if (loggerId != null)
            {
                return new DatadogiOSLogger(options.RemoteLogThreshold, options.RemoteSampleRate, loggerId);
            }

            return null;
        }

        public override void PlatformLog(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            // To serialize a non-object, we need to use JsonConvert, which isn't as optimized but supports
            // Dictionaries, where JsonUtility does not.
            var jsonAttributes = JsonConvert.SerializeObject(attributes);
            string jsonError = null;
            if (error != null)
            {
                var errorInfo = new Dictionary<string, string>()
                {
                    { "type", error.GetType()?.ToString() ?? string.Empty },
                    { "message", error.Message ?? string.Empty },
                    { "stackTrace", error.StackTrace?.ToString() ?? string.Empty },
                };
                jsonError = JsonConvert.SerializeObject(errorInfo);
            }

            DatadogLoggingBridge.DatadogLogging_Log(_loggerId, (int)level, message, jsonAttributes, jsonError);
        }

        public override void AddTag(string tag, string value = null)
        {
            DatadogLoggingBridge.DatadogLogging_AddTag(_loggerId, tag, value);
        }

        public override void RemoveTag(string tag)
        {
            DatadogLoggingBridge.DatadogLogging_RemoveTag(_loggerId, tag);
        }

        public override void RemoveTagsWithKey(string key)
        {
            DatadogLoggingBridge.DatadogLogging_RemoveTagWithKey(_loggerId, key);
        }

        public override void AddAttribute(string key, object value)
        {
            var jsonArg = new Dictionary<string, object>()
            {
                { key, value },
            };
            var jsonString = JsonConvert.SerializeObject(jsonArg);
            DatadogLoggingBridge.DatadogLogging_AddAttribute(_loggerId, jsonString);
        }

        public override void RemoveAttribute(string key)
        {
            DatadogLoggingBridge.DatadogLogging_RemoveAttribute(_loggerId, key);
        }
    }

    internal static class DatadogLoggingBridge
    {
        [DllImport("__Internal")]
        public static extern string DatadogLogging_CreateLogger(string optionsJson);

        [DllImport("__Internal")]
        public static extern void DatadogLogging_Log(string loggerId, int logLevel, string message, string attributes, string errorInfo);

        [DllImport("__Internal")]
        public static extern void DatadogLogging_AddTag(string loggerId, string tag, string value);

        [DllImport("__Internal")]
        public static extern void DatadogLogging_RemoveTag(string loggerId, string tag);

        [DllImport("__Internal")]
        public static extern void DatadogLogging_RemoveTagWithKey(string loggerId, string tag);

        [DllImport("__Internal")]
        public static extern void DatadogLogging_AddAttribute(string loggerId, string jsonAttribute);

        [DllImport("__Internal")]
        public static extern void DatadogLogging_RemoveAttribute(string loggerId, string tag);
    }
}
