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

        public override void AddTag(string tag, string value = null)
        {
            if (value != null)
            {
                _androidLogger.Call("addTag", tag, value);
            }
            else
            {
                _androidLogger.Call("addTag", tag);
            }
        }

        public override void Log(DdLogLevel level, string message, Dictionary<string, object> attributes, Exception error = null)
        {
            // TODO: RUMM-3272 - Support errors
            var androidLevel = DatadogConfigurationHelpers.DdLogLevelToAndroidLogLevel(level);

            using var javaAttributes = DictionaryToJavaMap(attributes);
            _androidLogger.Call("log", (int)androidLevel, message, null, null, null, javaAttributes);
        }

        private AndroidJavaObject DictionaryToJavaMap(IDictionary<string, object> attributes)
        {
            var javaMap = new AndroidJavaObject("java.util.HashMap");
            IntPtr putMethod = AndroidJNIHelper.GetMethodID(javaMap.GetRawClass(), "put",
                "(Ljava/lang/Object;Ljava/lang/Object;)Ljava/lang/Object;");

            if (attributes != null)
            {
                foreach (var item in attributes)
                {
                    using var javaKey = new AndroidJavaObject("java.lang.String", item.Key);
                    AndroidJavaObject javaValue;
                    switch (item.Value)
                    {
                        case int val:
                            javaValue = new AndroidJavaObject("java.lang.Integer", val);
                            break;
                        case string val:
                            javaValue = new AndroidJavaObject("java.lang.String", val);
                            break;
                        case float val:
                            javaValue = new AndroidJavaObject("java.lang.Float", val);
                            break;
                        case double val:
                            javaValue = new AndroidJavaObject("java.lang.Double", val);
                            break;
                        case bool val:
                            javaValue = new AndroidJavaObject("java.lang.Boolean", val);
                            break;
                        case IDictionary<string, object> val:
                            javaValue = DictionaryToJavaMap(val);
                            break;
                        default:
                            var value = item.Value != null ? item.Value.ToString() : "null";
                            javaValue = new AndroidJavaObject("java.lang.String", value);
                            break;
                    }

                    var args = new object[]
                    {
                        javaKey,
                        javaValue,
                    };
                    AndroidJNI.CallObjectMethod(javaMap.GetRawObject(), putMethod, AndroidJNIHelper.CreateJNIArgArray(args));
                }
            }

            return javaMap;
        }
    }
}
