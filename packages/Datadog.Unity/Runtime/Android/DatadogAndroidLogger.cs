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
    internal class DatadogAndroidLogger : DdLogger
    {
        private readonly AndroidJavaObject _androidLogger;
        private readonly Il2CppErrorProcessor _errorProcessor;

        private readonly jvalue[] _nativeErrorSourceAttributeArgs;

        public DatadogAndroidLogger(DdLogLevel logLevel, float sampleRate, IDatadogPlatform platform, AndroidJavaObject androidLogger)
            : base(logLevel, sampleRate)
        {
            _androidLogger = androidLogger;
            _errorProcessor = new Il2CppErrorProcessor(platform);

            // Cache the argument added to the attribute map to indicate that the source of the error is native
            _nativeErrorSourceAttributeArgs = AndroidJNIHelper.CreateJNIArgArray(
                new object[]
                {
                    new AndroidJavaObject("java.lang.String", DatadogSdk.ConfigKeys.NativeSourceType),
                    new AndroidJavaObject("java.lang.String", "ndk+il2cpp"),
                }
            );
        }

        public override void AddAttribute(string key, object value)
        {
            AndroidJavaObject javaValue = value == null ? null : DatadogAndroidHelpers.ObjectToJavaObject(value);
            _androidLogger.Call("addAttribute", key, javaValue);
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

        internal override void PlatformLog(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            var androidLevel = InternalHelpers.DdLogLevelToAndroidLogLevel(level);

            using var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);
            string errorKind = null;
            string errorMessage = null;
            string errorStack = null;
            if (error != null)
            {
                errorKind = error.GetType()?.ToString();
                errorMessage = error.Message;
                var nativeStackTrace = _errorProcessor.GetNativeStackTrace(error);
                if (nativeStackTrace != null)
                {
                    AndroidJNI.CallObjectMethod(
                        javaAttributes.GetRawObject(),
                        DatadogAndroidHelpers.hashMapPutMethodId,
                        _nativeErrorSourceAttributeArgs);
                }

                errorStack = nativeStackTrace ?? error.StackTrace ?? string.Empty;
            }

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
