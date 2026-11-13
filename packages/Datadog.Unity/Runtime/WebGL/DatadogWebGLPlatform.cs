// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: UnityEngine.Scripting.Preserve]
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Datadog.Unity.WebGL
{
    [Preserve]
    public static class DatadogInitialization
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void InitializeDatadog()
        {
            var options = DatadogConfigurationOptions.Load();
            if (options.Enabled)
            {
                var logsAvailable = DatadogWebGLLogs.IsAvailable;
                var rumAvailable = DatadogWebGLRum.IsAvailable;
                if (!logsAvailable)
                {
                    DatadogSdk.Instance.InternalLogger.Log(
                        DdLogLevel.Debug,
                        "Browser Logs SDK script is unavailable. Logs will use the no-op implementation.");
                }

                if (!rumAvailable)
                {
                    DatadogSdk.Instance.InternalLogger.Log(
                        DdLogLevel.Debug,
                        "Browser RUM SDK script is unavailable. RUM will use the no-op implementation.");
                }

                IDatadogPlatform platform = logsAvailable || rumAvailable
                    ? new DatadogWebGLPlatform(logsAvailable, rumAvailable)
                    : new DatadogNoOpPlatform();
                platform.Init(options);
                DatadogSdk.InitWithPlatform(platform, options);
            }
        }
    }

    internal class DatadogWebGLPlatform : IDatadogPlatform
    {
        private readonly DatadogWebGLLogs _logs;
        private readonly DatadogWebGLRum _rum;

        internal DatadogWebGLPlatform(bool logsAvailable, bool rumAvailable)
        {
            _logs = logsAvailable ? new DatadogWebGLLogs() : null;
            _rum = rumAvailable ? new DatadogWebGLRum() : null;
        }

        public void Init(DatadogConfigurationOptions options)
        {
            _logs?.Init(options);
        }

        public IRumContextProvider CreateRumContextProvider()
        {
            return NoOpRumContextProvider.Instance;
        }

        public DatadogWorker CreateWorker(IInternalLogger logger)
        {
            return new PassthroughWorker();
        }

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {
            // Not implemented on Web
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            var consent = trackingConsent.ToWebValue();
            _logs?.SetTrackingConsent(consent);
            _rum?.SetTrackingConsent(consent);
        }

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            return _logs != null ? _logs.CreateLogger(options) : new DdNoOpLogger();
        }

        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
            if (attributes == null)
            {
                return;
            }

            _logs?.AddGlobalAttributes(attributes);
        }

        public void RemoveLogsAttribute(string key)
        {
            if (key == null)
            {
                // Not an error, but don't bother calling to platform
                return;
            }

            _logs?.RemoveGlobalAttribute(key);
        }

        public void SetUserInfo(string id, string name, string email, Dictionary<string, object> extraInfo)
        {
            var jsonUserInfo = new Dictionary<string, object>();
            if (id != null)
            {
                jsonUserInfo["id"] = id;
            }

            if (name != null)
            {
                jsonUserInfo["name"] = name;
            }

            if (email != null)
            {
                jsonUserInfo["email"] = email;
            }

            foreach (var item in extraInfo)
            {
                jsonUserInfo[item.Key] = item.Value;
            }

            var jsonString = JsonConvert.SerializeObject(jsonUserInfo);
            _logs?.SetUserInfo(jsonString);
            _rum?.SetUserInfo(jsonString);
        }

        public void ClearUserInfo()
        {
            _logs?.ClearUserInfo();
            _rum?.ClearUserInfo();
        }

        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
            if (extraInfo == null)
            {
                // Don't bother calling to platform
                return;
            }

            var jsonAttributes = JsonConvert.SerializeObject(extraInfo);
            _logs?.AddUserExtraInfo(jsonAttributes);
            _rum?.AddUserExtraInfo(jsonAttributes);
        }

        public IDdRumInternal InitRum(DatadogConfigurationOptions options)
        {
            if (_rum == null)
            {
                return new DdNoOpRum();
            }

            _rum.Init(options);
            return _rum;
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

        public string GetNativeStack(Exception error)
        {
            return string.Empty;
        }
    }
}
