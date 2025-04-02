// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity.Logs;
using Newtonsoft.Json;

namespace Datadog.Unity.WebGL
{
    public class DatadogWebGLLogger : DdLogger
    {
        private readonly string _loggerId;

        internal DatadogWebGLLogger(DdLogLevel logLevel, float sampleRate, string loggerId)
            : base(logLevel, sampleRate)
        {
            _loggerId = loggerId;
        }

        public override void AddTag(string tag, string value = null)
        {
            // Not implemented on Web
        }

        public override void RemoveTag(string tag)
        {
            // Not implemented on Web
        }

        public override void RemoveTagsWithKey(string key)
        {
            // Not implemented on Web
        }

        public override void AddAttribute(string key, object value)
        {
            //throw new System.NotImplementedException();
        }

        public override void RemoveAttribute(string key)
        {
            //throw new System.NotImplementedException();
        }

        internal override void PlatformLog(DdLogLevel level, string message,
            Dictionary<string, object> attributes = null, Exception error = null)
        {
            var jsonAttributes = JsonConvert.SerializeObject(attributes);
            if (jsonAttributes == string.Empty)
            {
                // Require an empty object for JSON.parse to work
                jsonAttributes = "{}";
            }

            var webLogLevel = ToWebLogLevel(level);
            DDLog(
                _loggerId,
                message,
                webLogLevel,
                error?.GetType().ToString(),
                error?.Message,
                error?.StackTrace,
                jsonAttributes);
        }

        private static string ToWebLogLevel(DdLogLevel level)
        {
            return level switch
            {
                DdLogLevel.Debug => "debug",
                DdLogLevel.Info => "info",
                DdLogLevel.Notice => "warn",
                DdLogLevel.Warn => "warn",
                DdLogLevel.Error => "error",
                DdLogLevel.Critical => "error",
                _ => throw new ArgumentOutOfRangeException(nameof(level), level, null)
            };
        }

        #region External Javascript Methods

        [DllImport("__Internal")]
        private static extern void DDLog(string loggerId, string message, string level, string errorKind,
            string errorMessage, string errorStackTrace, string attributes);

        #endregion
    }
}
