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
        private readonly List<IntPtr> _loggers = new();
        private IntPtr _core;
        private IntPtr _logging;

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

                _logging = DatadogNative.InitLogging(_core);
                if (_logging == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to register the native Datadog Logs feature.");
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
                foreach (var logger in _loggers)
                {
                    DatadogNative.DestroyLogger(logger);
                }

                _loggers.Clear();
                DatadogNative.DestroyLogging(_logging);
                _logging = IntPtr.Zero;
                DatadogNative.DestroyCore(_core);
                _core = IntPtr.Zero;
            }
        }

        internal void WithCore(Action action)
        {
            lock (_coreLock)
            {
                if (_core != IntPtr.Zero)
                {
                    action();
                }
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
            };
        }

        public DatadogWorker CreateWorker(IInternalLogger logger) => new PassthroughWorker();

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            lock (_coreLock)
            {
                if (_core == IntPtr.Zero)
                {
                    return new DdNoOpLogger();
                }

                DatadogNative.InitLoggerConfiguration(out var configuration);
                DatadogNative.SetLoggerService(ref configuration, options.Service);
                DatadogNative.SetLoggerName(ref configuration, options.Name);
                DatadogNative.SetLoggerEnrichWithRumContext(ref configuration, options.BundleWithRumEnabled);
                var logger = DatadogNative.CreateLogger(_logging, ref configuration);
                if (logger == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create a native Datadog logger.");
                }

                _loggers.Add(logger);
                var innerLogger = new DatadogDesktopLogger(this, options.RemoteLogThreshold, options.RemoteSampleRate, logger);
                return new DdWorkerProxyLogger(worker, innerLogger);
            }
        }

        public IDdRumInternal InitRum(DatadogConfigurationOptions options) => new DdNoOpRum();

        public IRumContextProvider CreateRumContextProvider() => NoOpRumContextProvider.Instance;

        public string GetNativeStack(Exception error) => string.Empty;

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {
        }

        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
            if (attributes == null)
            {
                return;
            }

            WithCore(() =>
            {
                foreach (var attribute in attributes)
                {
                    using var value = new DatadogNativeAttribute(attribute.Value);
                    DatadogNative.AddLogsAttribute(_logging, attribute.Key, ref value.Value);
                }
            });
        }

        public void RemoveLogsAttribute(string key)
        {
            WithCore(() => DatadogNative.RemoveLogsAttribute(_logging, key));
        }

        public void SetUserInfo(string id, string name, string email, Dictionary<string, object> extraInfo)
        {
            WithCore(() =>
            {
                using var nativeExtraInfo = new DatadogNativeAttribute(extraInfo);
                DatadogNative.SetUserInfo(_core, id, name, email, ref nativeExtraInfo.Value);
            });
        }

        public void ClearUserInfo()
        {
            WithCore(() => DatadogNative.ClearUserInfo(_core));
        }

        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
            if (extraInfo == null)
            {
                return;
            }

            WithCore(() =>
            {
                using var nativeExtraInfo = new DatadogNativeAttribute(extraInfo);
                DatadogNative.AddUserExtraInfo(_core, ref nativeExtraInfo.Value);
            });
        }

        public void SendDebugTelemetry(string message)
        {
        }

        public void SendDebugTelemetry(string message, IDictionary<string, object> attributes)
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
