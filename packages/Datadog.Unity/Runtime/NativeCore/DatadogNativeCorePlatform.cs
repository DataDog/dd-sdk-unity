// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity.Logs;
using Datadog.Unity.Worker;
using MessagePack;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: UnityEngine.Scripting.Preserve]
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Datadog.Unity.NativeCore
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
                DatadogSdk.InitWithPlatform(new DatadogNativeCorePlatform(), options);
            }
        }
    }

    internal class DatadogNativeCorePlatform : IDatadogPlatform
    {
        private IntPtr _corePtr;

        public void Init(DatadogConfigurationOptions options)
        {
            _corePtr = DatadogNativeCoreFFI.CoreCreate(new ()
            {
                CoreDirectory = "temp",
                ClientToken = options.ClientToken,
                Env = "prod",
                Service = "com.datadoghq.unity.example",
            });

            if (_corePtr != IntPtr.Zero)
            {
                var loggingConfig = new DatadogNativeCoreFFI.CFeatureConfiguration()
                {
                    Name = "logs",
                    Endpoint = "https://browser-intake-datadoghq.com/api/v2/logs",
                    DataUploadFormat = new ()
                    {
                        Prefix = "[",
                        Suffix = "]",
                        Separator = ",",
                    },
                };
                DatadogNativeCoreFFI.CoreCreateFeature(_corePtr, loggingConfig);
            }
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {

        }

        public IDdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            return new DdWorkerProxyLogger(worker, new DatadogNativeLogger(this, options));
        }

        public void SendMessage(CoreMessage message)
        {
            var bytes = MessagePackSerializer.Serialize(message);
            DatadogNativeCoreFFI.CoreSendMessage(_corePtr, bytes, bytes.Length);
        }
    }

    [MessagePackObject]
    public class CoreMessage
    {
        [Key("feature_target")]
        public string FeatureTarget { get; private set; }

        [Key("context_changes")]
        public Dictionary<string, object> ContextChanges { get; private set; }

        [Key("message_data")]
        public string MessageData { get; private set; }

        public CoreMessage(string featureTarget, Dictionary<string, object> contextChanges, string messageData)
        {
            FeatureTarget = featureTarget;
            ContextChanges = contextChanges;
            MessageData = messageData;
        }
    }

    internal static class DatadogNativeCoreFFI
    {
        private const string DllName = "dd_native_rum";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class CCoreConfiguration
        {
            public string CoreDirectory;
            public string ClientToken;
            public string Env;
            public string Service;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class CDataUploadFormat
        {
            public string Prefix;
            public string Suffix;
            public string Separator;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class CFeatureConfiguration
        {
            public string Name;
            public string Endpoint;
            public CDataUploadFormat DataUploadFormat;
        }

        [DllImport(DllName, EntryPoint = "datadog_core_create")]
        public static extern IntPtr CoreCreate(CCoreConfiguration configuration);

        [DllImport(DllName, EntryPoint = "datadog_core_create_feature")]
        public static extern void CoreCreateFeature(IntPtr core, CFeatureConfiguration configuration);

        [DllImport(DllName, EntryPoint = "datadog_core_send_message")]
        public static extern void CoreSendMessage(IntPtr core, byte[] buffer, int length);
    }
}
