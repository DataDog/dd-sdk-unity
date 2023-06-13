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

        public override void RemoveAttribute(string key)
        {
            _androidLogger.Call("removeAttribute", key);
        }

        public override void RemoveTag(string tag)
        {
            _androidLogger.Call("removeTag", tag);
        }

        public override void RemoveTagWithKey(string key)
        {
            _androidLogger.Call("removeTagsWithKey", key);
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
                    var javaValue = ObjectToJavaObject(item.Value);

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

        private AndroidJavaObject ObjectToJavaObject(object value)
        {
            AndroidJavaObject javaValue;
            switch (value)
            {
                // Integer types - okay to convert all of these to Java ints
                case byte val:
                    javaValue = new AndroidJavaObject("java.lang.Integer", (int)val);
                    break;
                case sbyte val:
                    javaValue = new AndroidJavaObject("java.lang.Integer", (int)val);
                    break;
                case short val:
                    javaValue = new AndroidJavaObject("java.lang.Integer", (int)val);
                    break;
                case ushort val:
                    javaValue = new AndroidJavaObject("java.lang.Integer", (int)val);
                    break;
                case char val:
                    javaValue = new AndroidJavaObject("java.lang.Integer", (int)val);
                    break;
                case int val:
                    javaValue = new AndroidJavaObject("java.lang.Integer", val);
                    break;
                case uint val:
                    // Pass unisgned int as a long to avoid potential overflow
                    javaValue = new AndroidJavaObject("java.lang.Long", (long)val);
                    break;
                case nint val:
                    javaValue = new AndroidJavaObject("java.lang.Long", (long)val);
                    break;
                case nuint val:
                    javaValue = new AndroidJavaObject("java.lang.Long", (long)val);
                    break;
                case long val:
                    javaValue = new AndroidJavaObject("java.lang.Long", val);
                    break;
                case ulong val:
                    // Potential loss of precision here
                    javaValue = new AndroidJavaObject("java.lang.Long", val);
                    break;
                case string val:
                    javaValue = new AndroidJavaObject("java.lang.String", val);
                    break;
                case decimal val:
                    javaValue = new AndroidJavaObject("java.lang.Double", (double)val);
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
                // TODO: Need to support lists / arrays
                case IDictionary<string, object> val:
                    javaValue = DictionaryToJavaMap(val);
                    break;
                default:
                    var strValue = value != null ? value.ToString() : "null";
                    javaValue = new AndroidJavaObject("java.lang.String", strValue);
                    break;
            }

            return javaValue;
        }
    }
}
