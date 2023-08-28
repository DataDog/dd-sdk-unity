// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Runtime.CompilerServices;
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
    // These are mappings to android.util.Log
    internal enum AndroidLogLevel
    {
        Verbose = 2,
        Debug = 3,
        Info = 4,
        Warn = 5,
        Error = 6,
        Assert = 7,
    }

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

        public DatadogAndroidPlatform()
        {
            _datadogClass = new AndroidJavaClass("com.datadog.android.Datadog");
        }

        public void Init(DatadogConfigurationOptions options)
        {
            var applicationId = options.RumApplicationId == string.Empty ? null : options.RumApplicationId;

            using var credentials = new AndroidJavaObject(
                "com.datadog.android.core.configuration.Credentials",
                options.ClientToken,
                "prod",
                string.Empty,     // variant
                applicationId,
                null);            // serviceName
            using var configBuilder = new AndroidJavaObject(
                "com.datadog.android.core.configuration.Configuration$Builder",
                true,       // logsEnabled
                false,                // tracesEnabled
                true,                 // crashReportsEnabled
                options.RumEnabled);
            configBuilder.Call<AndroidJavaObject>("useSite", DatadogConfigurationHelpers.GetSite(options.Site));
            configBuilder.Call<AndroidJavaObject>("setBatchSize", DatadogConfigurationHelpers.GetBatchSize(options.BatchSize));
            configBuilder.Call<AndroidJavaObject>("setUploadFrequency", DatadogConfigurationHelpers.GetUploadFrequency(options.UploadFrequency));

            using var crashPlugin = new AndroidJavaObject("com.datadog.android.ndk.NdkCrashReportsPlugin");
            using var featureEnum = new AndroidJavaClass("com.datadog.android.plugin.Feature");
            using var feature = featureEnum.GetStatic<AndroidJavaObject>("CRASH");
            configBuilder.Call<AndroidJavaObject>("addPlugin", crashPlugin, feature);

            if (options.CustomEndpoint != string.Empty)
            {
                configBuilder.Call<AndroidJavaObject>("useCustomLogsEndpoint", options.CustomEndpoint);
                configBuilder.Call<AndroidJavaObject>("useCustomCrashReportsEndpoint", options.CustomEndpoint + "/logs");
                configBuilder.Call<AndroidJavaObject>("useCustomRumEndpoint", options.CustomEndpoint + "/rum");
            }

            if (options.RumEnabled)
            {
                configBuilder.Call<AndroidJavaObject>("disableInteractionTracking");
                IntPtr useViewTrackingStrategyMethod = AndroidJNIHelper.GetMethodID(
                    configBuilder.GetRawClass(),
                    "useViewTrackingStrategy",
                    "(Lcom/datadog/android/rum/tracking/ViewTrackingStrategy;)Lcom/datadog/android/core/configuration/Configuration$Builder;"
                );
                var args = new object[] { null };
                AndroidJNI.CallObjectMethod(configBuilder.GetRawObject(), useViewTrackingStrategyMethod, AndroidJNIHelper.CreateJNIArgArray(args));
            }

            var configuration = configBuilder.Call<AndroidJavaObject>("build");

            _datadogClass.CallStatic(
                "initialize",
                GetApplicationContext(),
                credentials,
                configuration,
                DatadogConfigurationHelpers.GetTrackingConsent(TrackingConsent.Granted));
            _datadogClass.CallStatic("setVerbosity", (int)AndroidLogLevel.Verbose);
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            _datadogClass.CallStatic("setTrackingConsent", DatadogConfigurationHelpers.GetTrackingConsent(trackingConsent));
        }

        public IDdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker)
        {
            using var loggerBuilder = new AndroidJavaObject("com.datadog.android.log.Logger$Builder");
            if (options.ServiceName != null)
            {
                loggerBuilder.Call<AndroidJavaObject>("setServiceName", options.ServiceName);
            }

            if (options.LoggerName != null)
            {
                loggerBuilder.Call<AndroidJavaObject>("setLoggerName", options.LoggerName);
            }

            loggerBuilder.Call<AndroidJavaObject>("setNetworkInfoEnabled", options.SendNetworkInfo);
            loggerBuilder.Call<AndroidJavaObject>("setDatadogLogsEnabled", options.SendToDatadog);
            var androidReportingThreshold = (int)DatadogConfigurationHelpers.DdLogLevelToAndroidLogLevel(options.DatadogReportingThreshold);
            loggerBuilder.Call<AndroidJavaObject>("setDatadogLogsMinPriority", androidReportingThreshold);

            var androidLogger = loggerBuilder.Call<AndroidJavaObject>("build");

            var innerLogger = new DatadogAndroidLogger(androidLogger);
            return new DdWorkerProxyLogger(worker, innerLogger);
        }

        public IDdRum InitRum(DatadogConfigurationOptions options)
        {
            using var rumMonitorBuilder = new AndroidJavaObject("com.datadog.android.rum.RumMonitor$Builder");
            var rum = rumMonitorBuilder.Call<AndroidJavaObject>("build");
            return new DatadogAndroidRum(rum);
        }

        private AndroidJavaObject GetApplicationContext()
        {
            using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            return currentActivity.Call<AndroidJavaObject>("getApplicationContext");
        }
    }
}
