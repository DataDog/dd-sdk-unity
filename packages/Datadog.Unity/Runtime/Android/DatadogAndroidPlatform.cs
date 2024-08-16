// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using UnityEngine;
using UnityEngine.Scripting;

[assembly: UnityEngine.Scripting.Preserve]
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: InternalsVisibleTo("com.datadoghq.unity.tests")]

namespace Datadog.Unity.Android
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
                var datadogPlatform = new DatadogAndroidPlatform();
                datadogPlatform.Init(options);
                DatadogSdk.InitWithPlatform(datadogPlatform, options);
            }
        }
    }

    internal class DatadogAndroidPlatform : IDatadogPlatform
    {
        private AndroidJavaClass _datadogClass;
        private bool _shouldTranslateCsStacks = false;

        public DatadogAndroidPlatform()
        {
            _datadogClass = new AndroidJavaClass("com.datadog.android.Datadog");
        }

        public void Init(DatadogConfigurationOptions options)
        {
            var applicationId = options.RumApplicationId == string.Empty ? null : options.RumApplicationId;
            SetVerbosity(options.SdkVerbosity);

            // Debug builds have full file / line info and should be translated, and if you're not outputting symbols
            // there will be no way to perform the translation, so avoid it.
            _shouldTranslateCsStacks = options.OutputSymbols && !Debug.isDebugBuild;

            var environment = options.Env;
            if (environment is null or "")
            {
                environment = "prod";
            }

            var serviceName = options.ServiceName == string.Empty ? null : options.ServiceName;

            using var configBuilder = new AndroidJavaObject(
                "com.datadog.android.core.configuration.Configuration$Builder",
                options.ClientToken,
                environment,
                string.Empty, // Variant Name
                serviceName // Service Name
            );

            configBuilder.Call<AndroidJavaObject>("useSite", DatadogConfigurationHelpers.GetSite(options.Site));
            configBuilder.Call<AndroidJavaObject>("setBatchSize", DatadogConfigurationHelpers.GetBatchSize(options.BatchSize));
            configBuilder.Call<AndroidJavaObject>("setUploadFrequency", DatadogConfigurationHelpers.GetUploadFrequency(options.UploadFrequency));
            configBuilder.Call<AndroidJavaObject>("setBatchProcessingLevel", DatadogConfigurationHelpers.GetBatchProcessingLevel(options.BatchProcessingLevel));

            var additionalConfig = new Dictionary<string, object>()
            {
                { DatadogSdk.ConfigKeys.Source, "unity" },
                { DatadogSdk.ConfigKeys.NativeSourceType, "ndk+il2cpp" },
                { DatadogSdk.ConfigKeys.SdkVersion, DatadogSdk.SdkVersion },
            };

            configBuilder.Call<AndroidJavaObject>("setAdditionalConfiguration", DatadogAndroidHelpers.DictionaryToJavaMap(additionalConfig));

#if DEBUG
            if (options.CustomEndpoint != string.Empty && options.CustomEndpoint.StartsWith("http://"))
            {
                using var internalProxyClass = new AndroidJavaClass("com.datadog.android._InternalProxy");
                using var proxyInstance = internalProxyClass.GetStatic<AndroidJavaObject>("Companion");
                proxyInstance.Call<AndroidJavaObject>("allowClearTextHttp", configBuilder);
            }
#endif

            using var configuration = configBuilder.Call<AndroidJavaObject>("build");
            _datadogClass.CallStatic<AndroidJavaObject>(
                "initialize",
                GetApplicationContext(),
                configuration,
                DatadogConfigurationHelpers.GetTrackingConsent(TrackingConsent.Pending));

            // Configure logging
            using var logsConfigBuilder = new AndroidJavaObject("com.datadog.android.log.LogsConfiguration$Builder");
            if (options.CustomEndpoint != string.Empty)
            {
                logsConfigBuilder.Call<AndroidJavaObject>("useCustomEndpoint", options.CustomEndpoint);
            }

            using var logsConfig = logsConfigBuilder.Call<AndroidJavaObject>("build");
            using var logsClass = new AndroidJavaClass("com.datadog.android.log.Logs");
            logsClass.CallStatic("enable", logsConfig);

            if (options.RumEnabled)
            {
                using var rumConfigBuilder = new AndroidJavaObject("com.datadog.android.rum.RumConfiguration$Builder", options.RumApplicationId);
                rumConfigBuilder.Call<AndroidJavaObject>("disableUserInteractionTracking");
                if (options.CustomEndpoint != string.Empty)
                {
                    rumConfigBuilder.Call<AndroidJavaObject>("useCustomEndpoint", options.CustomEndpoint);
                }

                rumConfigBuilder.Call<AndroidJavaObject>("useViewTrackingStrategy", new object[] { null });
                rumConfigBuilder.Call<AndroidJavaObject>("setSessionSampleRate", options.SessionSampleRate);
                rumConfigBuilder.Call<AndroidJavaObject>("setTelemetrySampleRate", options.TelemetrySampleRate);

                if (options.VitalsUpdateFrequency != VitalsUpdateFrequency.None)
                {
                    using var updateFrequency = DatadogConfigurationHelpers.GetVitalsUpdateFrequency(options.VitalsUpdateFrequency);
                    rumConfigBuilder.Call<AndroidJavaObject>("setVitalsUpdateFrequency", updateFrequency);
                }

                switch (options.TrackNonFatalAnrs)
                {
                    case NonFatalAnrDetectionOption.Disabled:
                        rumConfigBuilder.Call<AndroidJavaObject>("trackNonFatalAnrs", false);
                        break;
                    case NonFatalAnrDetectionOption.Enabled:
                        rumConfigBuilder.Call<AndroidJavaObject>("trackNonFatalAnrs", true);
                        break;
                    case NonFatalAnrDetectionOption.SdkDefault:
                        // Don't do anything. SDK will take care of it.
                        break;
                }

                using var internalBuilder = new AndroidJavaClass("com.datadog.android.rum._RumInternalProxy");
                using var internalBuilderCompanion = internalBuilder.GetStatic<AndroidJavaObject>("Companion");
                internalBuilderCompanion.Call<AndroidJavaObject>("setTelemetryConfigurationEventMapper", rumConfigBuilder, new TelemetryCallback());

                // Uncomment to always send Configuraiton telemetry
                // var rumAdditionalConfig = new Dictionary<string, object>()
                // {
                //     { "_dd.telemetry.configuration_sample_rate", 100.0f },
                // };
                // internalBuilderCompanion.Call<AndroidJavaObject>("setAdditionalConfiguration",
                //     rumConfigBuilder,
                //     DatadogAndroidHelpers.DictionaryToJavaMap(rumAdditionalConfig));

                using var rumConfig = rumConfigBuilder.Call<AndroidJavaObject>("build");
                using var rumClass = new AndroidJavaClass("com.datadog.android.rum.Rum");
                rumClass.CallStatic("enable", rumConfig);
            }

            if (options.CrashReportingEnabled)
            {
                using var crashReportClass = new AndroidJavaClass("com.datadog.android.ndk.NdkCrashReports");
                crashReportClass.CallStatic("enable");
            }
        }

        public void SetVerbosity(CoreLoggerLevel logLevel)
        {
            _datadogClass.CallStatic("setVerbosity", DatadogConfigurationHelpers.GetAndroidLogLevel(logLevel));
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            _datadogClass.CallStatic("setTrackingConsent", DatadogConfigurationHelpers.GetTrackingConsent(trackingConsent));
        }

        public void SetUserInfo(string id, string name, string email, Dictionary<string, object> extraInfo)
        {
            var javaExtraInfo = DatadogAndroidHelpers.DictionaryToJavaMap(extraInfo);
            _datadogClass.CallStatic("setUserInfo", id, name, email, javaExtraInfo);
        }

        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
            if (extraInfo == null)
            {
                // Don't bother calling to platform
                return;
            }

            var javaExtraInfo = DatadogAndroidHelpers.DictionaryToJavaMap(extraInfo);
            _datadogClass.CallStatic("addUserExtraInfo", javaExtraInfo);
        }

        public DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            using var loggerBuilder = new AndroidJavaObject("com.datadog.android.log.Logger$Builder");
            if (options.Service != null)
            {
                loggerBuilder.Call<AndroidJavaObject>("setService", options.Service);
            }

            if (options.Name != null)
            {
                loggerBuilder.Call<AndroidJavaObject>("setName", options.Name);
            }

            loggerBuilder.Call<AndroidJavaObject>("setNetworkInfoEnabled", options.NetworkInfoEnabled);
            loggerBuilder.Call<AndroidJavaObject>("setBundleWithRumEnabled", options.BundleWithRumEnabled);
            var androidLogger = loggerBuilder.Call<AndroidJavaObject>("build");

            var innerLogger = new DatadogAndroidLogger(options.RemoteLogThreshold, options.RemoteSampleRate, this, androidLogger);
            return new DdWorkerProxyLogger(worker, innerLogger);
        }

        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
            using var logsClass = new AndroidJavaClass("com.datadog.android.log.Logs");
            foreach (var attr in attributes)
            {
                AndroidJavaObject javaValue = DatadogAndroidHelpers.ObjectToJavaObject(attr.Value);
                logsClass.CallStatic("addAttribute", attr.Key, javaValue);
            }
        }

        public void RemoveLogsAttribute(string key)
        {
            using var logsClass = new AndroidJavaClass("com.datadog.android.log.Logs");
            logsClass.CallStatic("removeAttribute", key);
        }

        public IDdRum InitRum(DatadogConfigurationOptions options)
        {
            using var globalRumMonitorClass = new AndroidJavaClass("com.datadog.android.rum.GlobalRumMonitor");
            var rum = globalRumMonitorClass.CallStatic<AndroidJavaObject>("get");

            return new DatadogAndroidRum(this, rum);
        }

        public void SendDebugTelemetry(string message)
        {
            using var proxy = GetInternalProxy();
            using AndroidJavaObject telemetry = proxy.Call<AndroidJavaObject>("get_telemetry");
            telemetry.Call("debug", message);
        }

        public void SendErrorTelemetry(string message, string stack, string kind)
        {
            using var proxy = GetInternalProxy();
            using AndroidJavaObject telemetry = proxy.Call<AndroidJavaObject>("get_telemetry");
            telemetry.Call("error", message, stack, kind);
        }

        public void ClearAllData()
        {
            _datadogClass.CallStatic("clearAllData");
        }

        public string GetNativeStack(Exception error)
        {
            // Don't perform this action if Datadog wasn't instructed to output symbols
            if (!_shouldTranslateCsStacks || error is null)
            {
                return null;
            }

            string resultStack = null;
            try
            {
                if (Il2CppErrorHelper.GetNativeStackFrames(
                        error,
                        out IntPtr[] frames,
                        out string imageUuid,
                        out string imageName))
                {
                    if (string.IsNullOrEmpty(imageName))
                    {
                        // Sometimes, Unity returns nothing in the imageName, sometimes it returns the name with
                        // an extra letter. We'll need to replace this with an actual name / address lookup at some point.
                        // TODO: RUM-4403 Add support for getting image name from UUID
                        imageName = "il2cpp.so";
                    }

                    // imageName comes back with an extra letter at the end, so we need to remove it (bug in Unity?)
                    if (!imageName.EndsWith(".so"))
                    {
                        imageName = imageName.Substring(0, imageName.Length - 1);
                    }

                    // Format of Android Native (NDK) stack trace is:
                    // #<frame number>  <pc> <address_offset>  <library_name>
                    // It can optionally include <symbol_name>+<symbol_offset> at the end but we won't include those.
                    var stackBuilder = new StringBuilder();
                    for (int i = 0; i < frames.Length; ++i)
                    {
                        var frame = frames[i].ToInt64();

                        // Currently assuming the frames are all relative. This should also be fixed by RUM-4403
                        stackBuilder.AppendLine($"#{i:D2}  pc {frame:x8}  {imageName}");
                    }

                    resultStack = stackBuilder.ToString();
                }
            }
            catch(Exception e)
            {
                SendErrorTelemetry("Failed to get native stack", e.StackTrace, e.GetType().ToString());
            }

            return resultStack;
        }

        private AndroidJavaObject GetApplicationContext()
        {
            using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            return currentActivity.Call<AndroidJavaObject>("getApplicationContext");
        }

        private AndroidJavaObject GetInternalProxy()
        {
            using AndroidJavaObject datadogInstance = _datadogClass.GetStatic<AndroidJavaObject>("INSTANCE");
            AndroidJavaObject internalProxy = datadogInstance.Call<AndroidJavaObject>("_internalProxy", new object[] { null });

            return internalProxy;
        }
    }
}
