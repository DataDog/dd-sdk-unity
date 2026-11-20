// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Runtime.InteropServices;
using Datadog.Unity.Logs;

namespace Datadog.Unity.Desktop
{
    internal static partial class DatadogNative
    {
        [DllImport("ddsdkcpp", EntryPoint = "dd_logging_init", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr InitLogging(IntPtr core);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logging_destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void DestroyLogging(IntPtr logging);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logging_add_attribute", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void AddLogsAttribute(IntPtr logging, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, ref Attribute value);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logging_remove_attribute", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void RemoveLogsAttribute(IntPtr logging, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_config_init", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void InitLoggerConfiguration(out LoggerConfiguration configuration);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_config_set_service", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetLoggerService(ref LoggerConfiguration configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string service);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_config_set_name", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetLoggerName(ref LoggerConfiguration configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_config_set_enrich_with_rum_context", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetLoggerEnrichWithRumContext(ref LoggerConfiguration configuration, [MarshalAs(UnmanagedType.I1)] bool enabled);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_create", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr CreateLogger(IntPtr logging, [In] ref LoggerConfiguration configuration);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void DestroyLogger(IntPtr logger);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_add_attribute", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void AddLoggerAttribute(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, ref Attribute value);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_remove_attribute", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void RemoveLoggerAttribute(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_add_tag", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void AddLoggerTag(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string tag);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_remove_tag", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void RemoveLoggerTag(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string tag);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_remove_tags_with_key", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void RemoveLoggerTagsWithKey(IntPtr logger, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_log", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void Log(IntPtr logger, DdLogLevel level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message,
            ref LogError error, ref Attribute attributes);

        [DllImport("ddsdkcpp", EntryPoint = "dd_logger_log", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void Log(IntPtr logger, DdLogLevel level, [MarshalAs(UnmanagedType.LPUTF8Str)] string message,
            IntPtr error, ref Attribute attributes);

        [StructLayout(LayoutKind.Sequential)]
        internal struct LoggerConfiguration
        {
            internal uint Version;
            internal float RemoteSampleRate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            internal byte[] Service;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            internal byte[] Name;
            internal DdLogLevel RemoteLogThreshold;
            internal UIntPtr InitialAttributeCapacity;
            [MarshalAs(UnmanagedType.I1)]
            internal bool EnrichWithRumContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct LogError
        {
            [MarshalAs(UnmanagedType.LPUTF8Str)]
            internal string Message;
            [MarshalAs(UnmanagedType.LPUTF8Str)]
            internal string Kind;
            [MarshalAs(UnmanagedType.LPUTF8Str)]
            internal string Stack;
        }
    }
}
