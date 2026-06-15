// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;

namespace Datadog.Unity.Desktop
{
    // Mirrors dd_log_error_t from dd-sdk-cpp include-c/datadog/logging.h.
    // Both fields are raw const char* pointers — callers must keep them valid for the duration of the P/Invoke call.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DdLogError
    {
        public IntPtr kind;  // error.kind
        public IntPtr stack; // error.stack
    }

    internal class DatadogDesktopLogger : DdLogger
    {
        private readonly IntPtr _logger;
        private readonly IInternalLogger _internalLogger;

        private DatadogDesktopLogger(IntPtr logger, DdLogLevel logLevel, float sampleRate, IInternalLogger internalLogger)
            : base(logLevel, sampleRate)
        {
            _logger = logger;
            _internalLogger = internalLogger;
        }

        internal static DatadogDesktopLogger Create(IntPtr logging, DatadogLoggingOptions options, IInternalLogger internalLogger)
        {
            // dd_logger_config_t uses fixed char[] buffers for name/service, so setters copy strings
            // and there is no string-lifetime issue between separate P/Invoke calls.
            var configPtr = Marshal.AllocHGlobal(512);
            IntPtr loggerPtr;
            try
            {
                dd_logger_config_init(configPtr);
                if (!string.IsNullOrEmpty(options.Name))
                {
                    dd_logger_config_set_name(configPtr, options.Name);
                }
                if (!string.IsNullOrEmpty(options.Service))
                {
                    dd_logger_config_set_service(configPtr, options.Service);
                }
                dd_logger_config_set_remote_sample_rate(configPtr, options.RemoteSampleRate);
                dd_logger_config_set_remote_log_threshold(configPtr, (int)options.RemoteLogThreshold);
                dd_logger_config_set_enrich_with_rum_context(configPtr, options.BundleWithRumEnabled);

                loggerPtr = dd_logger_create(logging, configPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(configPtr);
            }

            if (loggerPtr == IntPtr.Zero) return null;
            return new DatadogDesktopLogger(loggerPtr, options.RemoteLogThreshold, options.RemoteSampleRate, internalLogger);
        }

        internal override void PlatformLog(DdLogLevel level, string message, Dictionary<string, object> attributes = null, ErrorInfo error = null)
        {
            if (_logger == IntPtr.Zero) return;

            var attrs = (attributes != null && attributes.Count > 0)
                ? DdAttributes.BuildAttributeObject(attributes, _internalLogger)
                : DdAttributes.dd_attribute_null();

            try
            {
                if (error != null)
                {
                    var kindPtr = DatadogDesktopPlatform.AllocUtf8(error.Type);
                    var stackPtr = DatadogDesktopPlatform.AllocUtf8(error.StackTrace);
                    var logError = new DdLogError { kind = kindPtr, stack = stackPtr };
                    try
                    {
                        dd_logger_log(_logger, (int)level, message, ref logError, ref attrs);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(kindPtr);
                        Marshal.FreeHGlobal(stackPtr);
                    }
                }
                else
                {
                    dd_logger_log_no_err(_logger, (int)level, message, IntPtr.Zero, ref attrs);
                }
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public override void AddTag(string tag, string value = null)
        {
            if (_logger == IntPtr.Zero) return;
            if (value != null)
                dd_logger_add_tag_kv(_logger, tag, value);
            else
                dd_logger_add_tag(_logger, tag);
        }

        public override void RemoveTag(string tag)
        {
            if (_logger == IntPtr.Zero) return;
            dd_logger_remove_tag(_logger, tag);
        }

        public override void RemoveTagsWithKey(string key)
        {
            if (_logger == IntPtr.Zero) return;
            dd_logger_remove_tags_with_key(_logger, key);
        }

        public override void AddAttribute(string key, object value)
        {
            if (_logger == IntPtr.Zero) return;
            var attr = DdAttributes.MakeAttribute(value, _internalLogger);
            try { dd_logger_add_attribute(_logger, key, ref attr); }
            finally { DdAttributes.dd_attribute_free(ref attr); }
        }

        public override void RemoveAttribute(string key)
        {
            if (_logger == IntPtr.Zero) return;
            dd_logger_remove_attribute(_logger, key);
        }

        // === P/Invoke: Logger config ===

        [DllImport("dd_native")] private static extern void dd_logger_config_init(IntPtr config);
        [DllImport("dd_native")] private static extern void dd_logger_config_set_name(IntPtr config, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport("dd_native")] private static extern void dd_logger_config_set_service(IntPtr config, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport("dd_native")] private static extern void dd_logger_config_set_remote_sample_rate(IntPtr config, float value);
        [DllImport("dd_native")] private static extern void dd_logger_config_set_remote_log_threshold(IntPtr config, int value);
        [DllImport("dd_native")] private static extern void dd_logger_config_set_enrich_with_rum_context(IntPtr config, [MarshalAs(UnmanagedType.I1)] bool value);

        // === P/Invoke: Logger ===

        [DllImport("dd_native")] private static extern IntPtr dd_logger_create(IntPtr logging, IntPtr config);
        [DllImport("dd_native")] private static extern void dd_logger_log(IntPtr logger, int level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message, ref DdLogError err, ref DdAttribute attributes);
        [DllImport("dd_native", EntryPoint = "dd_logger_log")] private static extern void dd_logger_log_no_err(IntPtr logger, int level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message, IntPtr err, ref DdAttribute attributes);
        [DllImport("dd_native")] private static extern void dd_logger_add_tag(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string tag);
        [DllImport("dd_native")] private static extern void dd_logger_add_tag_kv(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport("dd_native")] private static extern void dd_logger_remove_tag(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string tag);
        [DllImport("dd_native")] private static extern void dd_logger_remove_tags_with_key(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
        [DllImport("dd_native")] private static extern void dd_logger_add_attribute(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref DdAttribute value);
        [DllImport("dd_native")] private static extern void dd_logger_remove_attribute(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    }
}
