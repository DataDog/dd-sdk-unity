// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Logs;

namespace Datadog.Unity.Desktop
{
    internal sealed class DatadogDesktopLogger : DdLogger
    {
        private readonly DatadogDesktopPlatform _platform;
        private readonly IntPtr _logger;

        internal DatadogDesktopLogger(DatadogDesktopPlatform platform, DdLogLevel logLevel, float sampleRate, IntPtr logger)
            : base(logLevel, sampleRate)
        {
            _platform = platform;
            _logger = logger;
        }

        internal override void PlatformLog(DdLogLevel level, string message, Dictionary<string, object> attributes = null, ErrorInfo error = null)
        {
            _platform.WithCore(() =>
            {
                using var nativeAttributes = new DatadogNativeAttribute(attributes);
                if (error == null)
                {
                    DatadogNative.Log(_logger, level, message, IntPtr.Zero, ref nativeAttributes.Value);
                }
                else
                {
                    var nativeError = new DatadogNative.LogError
                    {
                        Message = error.Message,
                        Kind = error.Type,
                        Stack = error.StackTrace,
                    };
                    DatadogNative.Log(_logger, level, message, ref nativeError, ref nativeAttributes.Value);
                }
            });
        }

        public override void AddTag(string tag, string value = null)
        {
            _platform.WithCore(() => DatadogNative.AddLoggerTag(_logger, value == null ? tag : $"{tag}:{value}"));
        }

        public override void RemoveTag(string tag)
        {
            _platform.WithCore(() => DatadogNative.RemoveLoggerTag(_logger, tag));
        }

        public override void RemoveTagsWithKey(string key)
        {
            _platform.WithCore(() => DatadogNative.RemoveLoggerTagsWithKey(_logger, key));
        }

        public override void AddAttribute(string key, object value)
        {
            _platform.WithCore(() =>
            {
                using var nativeValue = new DatadogNativeAttribute(value);
                DatadogNative.AddLoggerAttribute(_logger, key, ref nativeValue.Value);
            });
        }

        public override void RemoveAttribute(string key)
        {
            _platform.WithCore(() => DatadogNative.RemoveLoggerAttribute(_logger, key));
        }
    }
}
