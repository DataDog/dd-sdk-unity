// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using UnityEngine;

namespace Datadog.Unity.Desktop
{
    // Mirrors dd_diagnostic_message_t from dd-sdk-cpp include-c/datadog/core.h.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DdDiagnosticMessage
    {
        public int Level;
        public IntPtr Text;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DdDiagnosticHandler(ref DdDiagnosticMessage message, IntPtr userdata);

    internal class DatadogDesktopPlatform : IDatadogPlatform
    {
        // Held statically to prevent GC collection while native code holds the function pointer.
        private static readonly DdDiagnosticHandler _diagnosticHandler = OnDiagnosticMessage;

        private IntPtr _core;
        private IntPtr _logging;
        private IntPtr _rum;
        private IInternalLogger _logger = new PassThroughInternalLogger();

        public void Init(DatadogConfigurationOptions options)
        {
            _core = CreateCore(options);
            if (_core == IntPtr.Zero)
            {
                return;
            }

            _logging = dd_logging_init(_core);

            if (options.RumEnabled && !string.IsNullOrEmpty(options.RumApplicationId))
            {
                _rum = CreateRumFeature(_core, options);
            }

            if (!dd_core_start(_core))
            {
                Debug.Log("Failed to initialize Datadog Desktop Platform");
            }
        }

        public DatadogWorker CreateWorker(IInternalLogger logger)
        {
            _logger = logger;
            return new ThreadedWorker(logger);
        }

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {
            // dd-sdk-cpp diagnostic threshold is a config-time setting; runtime changes are not supported.
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            if (_core == IntPtr.Zero)
            {
                return;
            }

            dd_core_set_tracking_consent(_core, (int)trackingConsent);
        }

        public void SetUserInfo(string id, string name, string email, Dictionary<string, object> extraInfo)
        {
            if (_core == IntPtr.Zero)
            {
                return;
            }

            if (extraInfo != null && extraInfo.Count > 0)
            {
                var attrs = DdAttributes.BuildAttributeObject(extraInfo, _logger);
                try
                {
                    dd_core_set_user_info(_core, id, name, email, ref attrs);
                }
                finally
                {
                    DdAttributes.dd_attribute_free(ref attrs);
                }
            }
            else
            {
                var nullAttr = DdAttributes.dd_attribute_null();
                dd_core_set_user_info(_core, id, name, email, ref nullAttr);
            }
        }

        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
            if (_core == IntPtr.Zero || extraInfo == null || extraInfo.Count == 0)
            {
                return;
            }

            var attrs = DdAttributes.BuildAttributeObject(extraInfo, _logger);
            try
            {
                dd_core_add_user_extra_info(_core, ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            if (_logging == IntPtr.Zero)
            {
                return new DdNoOpLogger();
            }

            var innerLogger = DatadogDesktopLogger.Create(_logging, options, _logger);
            if (innerLogger == null)
            {
                return new DdNoOpLogger();
            }

            return new DdWorkerProxyLogger(worker, innerLogger);
        }

        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
            if (_logging == IntPtr.Zero || attributes == null)
            {
                return;
            }

            foreach (var kv in attributes)
            {
                var attr = DdAttributes.MakeAttribute(kv.Value, _logger);
                try
                {
                    dd_logging_add_attribute(_logging, kv.Key, ref attr);
                }
                finally
                {
                    DdAttributes.dd_attribute_free(ref attr);
                }
            }
        }

        public void RemoveLogsAttribute(string key)
        {
            if (_logging == IntPtr.Zero || key == null) return;
            dd_logging_remove_attribute(_logging, key);
        }

        public IDdRumInternal InitRum(DatadogConfigurationOptions options)
        {
            if (_rum == IntPtr.Zero)
            {
                return new DdNoOpRum();
            }

            return new DatadogDesktopRum(_rum, _logger);
        }

        public void SendDebugTelemetry(string message)
        {
            // Not supported by dd-sdk-cpp C API.
        }

        public void SendErrorTelemetry(string message, string stack, string kind)
        {
            // Not supported by dd-sdk-cpp C API.
        }

        public void ClearAllData()
        {
            // Not supported by dd-sdk-cpp C API.
        }

        public string GetNativeStack(Exception error)
        {
            // Desktop platforms have managed stack traces; no native mapping needed.
            return null;
        }

        #region Internal Helpers

        [AOT.MonoPInvokeCallback(typeof(DdDiagnosticHandler))]
        private static void OnDiagnosticMessage(ref DdDiagnosticMessage message, IntPtr userdata)
        {
            var text = message.Text;
            if (text == IntPtr.Zero)
            {
                return;
            }

            var logType = message.Level switch
            {
                2 => LogType.Warning,  // DD_DIAGNOSTIC_LEVEL_WARNING
                3 => LogType.Error,    // DD_DIAGNOSTIC_LEVEL_ERROR
                _ => LogType.Log,      // DD_DIAGNOSTIC_LEVEL_DEBUG, DD_DIAGNOSTIC_LEVEL_STATUS
            };

            try
            {
                var stringValue = Marshal.PtrToStringUTF8(text);
                Debug.unityLogger.Log(logType, IInternalLogger.DatadogTag, stringValue);
            }
            catch (ExecutionEngineException e)
            {
                // Telemetry
            }
        }

        // Allocates a null-terminated UTF-8 string in unmanaged memory. Caller must free with Marshal.FreeHGlobal.
        internal static IntPtr AllocUtf8(string str)
        {
            if (str == null) return IntPtr.Zero;
            var bytes = Encoding.UTF8.GetBytes(str);
            var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr + bytes.Length, 0);
            return ptr;
        }

        private static int MapSite(DatadogSite site) =>
            site switch
            {
                DatadogSite.Us1 => 0, // DD_SITE_US1
                DatadogSite.Us3 => 1, // DD_SITE_US3
                DatadogSite.Us5 => 2, // DD_SITE_US5
                DatadogSite.Eu1 => 3, // DD_SITE_EU1
                DatadogSite.Ap1 => 4, // DD_SITE_AP1
                DatadogSite.Ap2 => 5, // DD_SITE_AP2
                DatadogSite.Us1Fed => 6, // DD_SITE_US1_FED
                _ => 0,
            };

        private static int MapDiagnosticLevel(CoreLoggerLevel level) =>
            level switch
            {
                CoreLoggerLevel.Debug => 0, // DD_DIAGNOSTIC_LEVEL_DEBUG
                CoreLoggerLevel.Warn => 2, // DD_DIAGNOSTIC_LEVEL_WARNING
                CoreLoggerLevel.Error => 3, // DD_DIAGNOSTIC_LEVEL_ERROR
                CoreLoggerLevel.Critical => 3, // DD_DIAGNOSTIC_LEVEL_ERROR
                _ => 2,
            };

        private static IntPtr CreateCore(DatadogConfigurationOptions options)
        {
            // dd_core_config_t's string fields are fixed-size inline char[] buffers; each setter
            // copies (and truncates) the value immediately, so these unmanaged buffers don't strictly
            // need to outlive their setter calls. We free them all together below for simplicity.
            var tokenPtr = AllocUtf8(options.ClientToken);
            var servicePtr = AllocUtf8(string.IsNullOrEmpty(options.ServiceName)
                ? Application.productName
                : options.ServiceName);
            var envPtr = AllocUtf8(options.Env);
            var versionPtr = AllocUtf8(Application.version);
            var storagePtr = AllocUtf8(Application.persistentDataPath);
            var sourcePtr = AllocUtf8("unity");
            var sdkVersionPtr = AllocUtf8(DatadogSdk.SdkVersion);

            // Over-allocate generously; dd_core_config_t is currently ~1450 bytes (mostly its
            // fixed-size string buffers), leaving headroom for it to grow across SDK versions.
            var configPtr = Marshal.AllocHGlobal(4096);
            IntPtr core;
            IntPtr customEndpointPtr = IntPtr.Zero;
            try
            {
                dd_core_config_init(configPtr, tokenPtr, servicePtr, envPtr);
                dd_core_config_set_version(configPtr, versionPtr);
                dd_core_config_set_application_storage_path(configPtr, storagePtr);
                dd_core_config_set_site(configPtr, MapSite(options.Site));
                dd_core_config_set_batch_size(configPtr, (int)options.BatchSize);
                dd_core_config_set_upload_frequency(configPtr, (int)options.UploadFrequency);
                dd_core_config_set_batch_processing_level(configPtr, (int)options.BatchProcessingLevel);
                dd_core_config_set_diagnostic_threshold(configPtr, MapDiagnosticLevel(options.SdkVerbosity));
                dd_core_config_set_diagnostic_handler(configPtr, _diagnosticHandler);
                dd_core_config_internal_set_source(configPtr, sourcePtr);
                dd_core_config_internal_set_sdk_version(configPtr, sdkVersionPtr);
                if (!string.IsNullOrEmpty(options.CustomEndpoint))
                {
                    customEndpointPtr = AllocUtf8(options.CustomEndpoint);
                    dd_core_config_internal_set_custom_endpoint_url(configPtr, customEndpointPtr);
                }

                core = dd_core_create(configPtr, (int)TrackingConsent.Pending);
            }
            finally
            {
                Marshal.FreeHGlobal(customEndpointPtr);
                Marshal.FreeHGlobal(configPtr);
                Marshal.FreeHGlobal(tokenPtr);
                Marshal.FreeHGlobal(servicePtr);
                Marshal.FreeHGlobal(envPtr);
                Marshal.FreeHGlobal(versionPtr);
                Marshal.FreeHGlobal(storagePtr);
                Marshal.FreeHGlobal(sourcePtr);
                Marshal.FreeHGlobal(sdkVersionPtr);
            }

            return core;
        }

        private static IntPtr CreateRumFeature(IntPtr core, DatadogConfigurationOptions options)
        {
            // dd_rum_config_t stores application_id as a parsed dd_uuid_t (16 bytes by value).
            // No raw pointer fields; safe to use with separate P/Invoke calls.
            var configPtr = Marshal.AllocHGlobal(256);
            IntPtr rum;
            try
            {
                dd_rum_config_init(configPtr, options.RumApplicationId);
                dd_rum_config_set_session_sample_rate(configPtr, options.SessionSampleRate);
                rum = dd_rum_init(core, configPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(configPtr);
            }

            return rum;
        }

        #endregion

        #region P/Invoke: Core config

        [DllImport("dd_native")]
        private static extern void dd_core_config_init(IntPtr config, IntPtr clientToken, IntPtr service, IntPtr env);

        [DllImport("dd_native")]
        private static extern void dd_core_config_set_version(IntPtr config, IntPtr value);

        [DllImport("dd_native")]
        private static extern void dd_core_config_set_application_storage_path(IntPtr config, IntPtr value);

        [DllImport("dd_native")]
        private static extern void dd_core_config_set_site(IntPtr config, int value);

        [DllImport("dd_native")]
        private static extern void dd_core_config_set_batch_size(IntPtr config, int value);

        [DllImport("dd_native")]
        private static extern void dd_core_config_set_upload_frequency(IntPtr config, int value);

        [DllImport("dd_native")]
        private static extern void dd_core_config_set_batch_processing_level(IntPtr config, int value);

        [DllImport("dd_native")]
        private static extern void dd_core_config_set_diagnostic_threshold(IntPtr config, int value);

        [DllImport("dd_native")]
        private static extern void dd_core_config_set_diagnostic_handler(IntPtr config, DdDiagnosticHandler handler);

        [DllImport("dd_native")]
        private static extern void dd_core_config_internal_set_source(IntPtr config, IntPtr value);

        [DllImport("dd_native")]
        private static extern void dd_core_config_internal_set_custom_endpoint_url(IntPtr config, IntPtr value);

        [DllImport("dd_native")]
        private static extern void dd_core_config_internal_set_sdk_version(IntPtr config, IntPtr value);

        #endregion

        #region P/Invoke: Core

        [DllImport("dd_native")]
        private static extern IntPtr dd_core_create(IntPtr config, int trackingConsent);

        [DllImport("dd_native")]
        private static extern bool dd_core_start(IntPtr core);

        [DllImport("dd_native")]
        private static extern void dd_core_stop(IntPtr core);

        [DllImport("dd_native")]
        private static extern void dd_core_destroy(IntPtr core);

        [DllImport("dd_native")]
        private static extern void dd_core_set_tracking_consent(IntPtr core, int value);

        [DllImport("dd_native")]
        private static extern void dd_core_set_user_info(IntPtr core, [MarshalAs(UnmanagedType.LPUTF8Str)] string id,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string email,
            ref DdAttribute extraInfo);

        [DllImport("dd_native")]
        private static extern void dd_core_add_user_extra_info(IntPtr core, ref DdAttribute attrs);

        #endregion

        #region P/Invoke: RUM config

        [DllImport("dd_native")]
        private static extern void dd_rum_config_init(IntPtr config,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string applicationId);

        [DllImport("dd_native")]
        private static extern void dd_rum_config_set_session_sample_rate(IntPtr config, float value);

        [DllImport("dd_native")]
        private static extern IntPtr dd_rum_init(IntPtr core, IntPtr config);

        [DllImport("dd_native")]
        private static extern void dd_rum_destroy(IntPtr rum);

        #endregion

        #region P/Invoke: Logging

        [DllImport("dd_native")]
        private static extern IntPtr dd_logging_init(IntPtr core);

        [DllImport("dd_native")]
        private static extern void dd_logging_destroy(IntPtr logging);

        [DllImport("dd_native")]
        private static extern void dd_logging_add_attribute(IntPtr logging,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref DdAttribute value);

        [DllImport("dd_native")]
        private static extern void dd_logging_remove_attribute(IntPtr logging,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        #endregion

    }
}
