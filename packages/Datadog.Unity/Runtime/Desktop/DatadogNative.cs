// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Runtime.InteropServices;

namespace Datadog.Unity.Desktop
{
    internal static partial class DatadogNative
    {
        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_create", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr CreateCoreConfiguration(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string clientToken,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string service,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string environment);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void DestroyCoreConfiguration(IntPtr configuration);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_application_storage_path", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreStoragePath(IntPtr configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_site", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreSite(IntPtr configuration, Site site);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_client_token", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreClientToken(IntPtr configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string clientToken);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_service", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreService(IntPtr configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string service);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_env", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreEnvironment(IntPtr configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string environment);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_version", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreVersion(IntPtr configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string version);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_variant", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreVariant(IntPtr configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string variant);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_batch_size", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreBatchSize(IntPtr configuration, BatchSize batchSize);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_upload_frequency", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreUploadFrequency(IntPtr configuration, UploadFrequency uploadFrequency);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_batch_processing_level", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreBatchProcessingLevel(IntPtr configuration, BatchProcessingLevel batchProcessingLevel);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_set_diagnostic_threshold", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreDiagnosticThreshold(IntPtr configuration, DiagnosticLevel level);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_internal_set_custom_endpoint_url", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreCustomEndpoint(IntPtr configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string endpoint);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_internal_set_source", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreSource(IntPtr configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string source);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_config_internal_set_sdk_version", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetCoreSdkVersion(IntPtr configuration, [MarshalAs(UnmanagedType.LPUTF8Str)] string version);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_create", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern IntPtr CreateCore(IntPtr configuration, TrackingConsent trackingConsent);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_start", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool StartCore(IntPtr core);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_stop", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void StopCore(IntPtr core);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_destroy", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void DestroyCore(IntPtr core);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_set_tracking_consent", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetTrackingConsent(IntPtr core, TrackingConsent trackingConsent);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_set_user_info", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void SetUserInfo(IntPtr core,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string email,
            ref Attribute extraInfo);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_clear_user_info", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void ClearUserInfo(IntPtr core);

        [DllImport("ddsdkcpp", EntryPoint = "dd_core_add_user_extra_info", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        internal static extern void AddUserExtraInfo(IntPtr core, ref Attribute extraInfo);

        internal enum Site
        {
            Us1,
            Us3,
            Us5,
            Eu1,
            Ap1,
            Ap2,
            Us1Fed,
            Us2Fed,
        }

        internal enum DiagnosticLevel
        {
            Debug,
            Status,
            Warning,
            Error,
        }
    }
}
