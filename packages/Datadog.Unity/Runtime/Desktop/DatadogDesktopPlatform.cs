// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.IO;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using UnityEngine;

namespace Datadog.Unity.Desktop
{
    internal sealed class DatadogDesktopPlatform : IDatadogPlatform, IDisposable
    {
        private readonly object _coreLock = new();
        private IntPtr _core;

        public void Init(DatadogConfigurationOptions options)
        {
            lock (_coreLock)
            {
                if (_core != IntPtr.Zero)
                {
                    return;
                }

                DatadogNative.InitCoreConfiguration(out var configuration, options.ClientToken, options.ServiceName, options.Env);
                Directory.CreateDirectory(Application.persistentDataPath);
                DatadogNative.SetCoreStoragePath(ref configuration, Application.persistentDataPath);
                DatadogNative.SetCoreSite(ref configuration, ToNativeSite(options.Site));
                DatadogNative.SetCoreVersion(ref configuration, Application.version);
                DatadogNative.SetCoreBatchSize(ref configuration, options.BatchSize);
                DatadogNative.SetCoreUploadFrequency(ref configuration, options.UploadFrequency);
                DatadogNative.SetCoreBatchProcessingLevel(ref configuration, options.BatchProcessingLevel);
                DatadogNative.SetCoreDiagnosticThreshold(ref configuration, options.SdkVerbosity switch
                {
                    CoreLoggerLevel.Debug => DatadogNative.DiagnosticLevel.Debug,
                    CoreLoggerLevel.Warn => DatadogNative.DiagnosticLevel.Warning,
                    _ => DatadogNative.DiagnosticLevel.Error,
                });
                DatadogNative.SetCoreSource(ref configuration, "unity");
                DatadogNative.SetCoreSdkVersion(ref configuration, DatadogSdk.SdkVersion);
                if (!string.IsNullOrEmpty(options.CustomEndpoint))
                {
                    DatadogNative.SetCoreCustomEndpoint(ref configuration, options.CustomEndpoint);
                }

                _core = DatadogNative.CreateCore(ref configuration, TrackingConsent.Pending);
                if (_core == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create the native Datadog SDK core.");
                }
            }
        }

        internal void Start()
        {
            lock (_coreLock)
            {
                if (_core == IntPtr.Zero || !DatadogNative.StartCore(_core))
                {
                    throw new InvalidOperationException("Failed to start the native Datadog SDK core.");
                }
            }
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            lock (_coreLock)
            {
                DatadogNative.SetTrackingConsent(_core, trackingConsent);
            }
        }

        public void Dispose()
        {
            lock (_coreLock)
            {
                if (_core == IntPtr.Zero)
                {
                    return;
                }

                DatadogNative.StopCore(_core);
                DatadogNative.DestroyCore(_core);
                _core = IntPtr.Zero;
            }
        }

        private static DatadogNative.Site ToNativeSite(DatadogSite site)
        {
            return site switch
            {
                DatadogSite.Us1 => DatadogNative.Site.Us1,
                DatadogSite.Us3 => DatadogNative.Site.Us3,
                DatadogSite.Us5 => DatadogNative.Site.Us5,
                DatadogSite.Eu1 => DatadogNative.Site.Eu1,
                DatadogSite.Us1Fed => DatadogNative.Site.Us1Fed,
                DatadogSite.Ap1 => DatadogNative.Site.Ap1,
                DatadogSite.Ap2 => DatadogNative.Site.Ap2,
                _ => DatadogNative.Site.Us1,
            };
        }

        public DatadogWorker CreateWorker(IInternalLogger logger) => new NoOpWorker();

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker) => new DdNoOpLogger();

        public IDdRumInternal InitRum(DatadogConfigurationOptions options) => new DdNoOpRum();

        public IRumContextProvider CreateRumContextProvider() => NoOpRumContextProvider.Instance;

        public string GetNativeStack(Exception error) => string.Empty;

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {
        }

        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
        }

        public void RemoveLogsAttribute(string key)
        {
        }

        public void SetUserInfo(string id, string name, string email, Dictionary<string, object> extraInfo)
        {
        }

        public void ClearUserInfo()
        {
        }

        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
        }

        public void SendDebugTelemetry(string message)
        {
        }

        public void SendErrorTelemetry(string message, string stack, string kind)
        {
        }

        public void ClearAllData()
        {
        }
    }
}
