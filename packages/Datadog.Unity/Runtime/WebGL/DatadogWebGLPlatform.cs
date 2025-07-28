// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
                var platform = new DatadogWebGLPlatform();
                platform.Init(options);
                DatadogSdk.InitWithPlatform(platform, options);
            }
        }
    }

    internal class DatadogWebGLPlatform : IDatadogPlatform
    {
        private DatadogWebGLLogs _logs = new DatadogWebGLLogs();

        public void Init(DatadogConfigurationOptions options)
        {
            _logs.Init(options);
        }

        public DatadogWorker CreateWorker()
        {
            return new PassthroughWorker();
        }

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {
            // Not implemented on Web
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            DDCore_SetTrackingConsent(trackingConsent.ToWebValue());
        }

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            var innerLogger = _logs.CreateLogger(options);
            return innerLogger;
        }

        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
            if (attributes == null)
            {
                return;
            }

            var jsonAttributes = JsonConvert.SerializeObject(attributes);
            DDLogs_AddGlobalAttributes(jsonAttributes);
        }

        public void RemoveLogsAttribute(string key)
        {
            if (key == null)
            {
                // Not an error, but don't bother calling to platform
                return;
            }

            DDLogs_RemoveGlobalAttribute(key);
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
            DDCore_SetUserInfo(jsonString);
        }

        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
            if (extraInfo == null)
            {
                // Don't bother calling to platform
                return;
            }

            var jsonAttributes = JsonConvert.SerializeObject(extraInfo);
            DDCore_SetUserProperties(jsonAttributes);
        }

        public IDdRumInternal InitRum(DatadogConfigurationOptions options)
        {
            var rum = new DatadogWebGLRum();
            rum.Init(options);

            return rum;
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

        [DllImport("__Internal")]
        private static extern void DDLogs_AddGlobalAttributes(string jsonAttributes);

        [DllImport("__Internal")]
        private static extern void DDLogs_RemoveGlobalAttribute(string key);

        [DllImport("__Internal")]
        private static extern void DDCore_SetUserInfo(string jsonUserInfo);

        [DllImport("__Internal")]
        private static extern void DDCore_SetUserProperties(string jsonUserInfo);

        [DllImport("__Internal")]
        private static extern void DDCore_SetTrackingConsent(string trackingConsent);
    }
}
