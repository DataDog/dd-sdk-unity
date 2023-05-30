// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Runtime.CompilerServices;
using Datadog.Unity.Logs;
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
            var datadogPlatform = new DatadogAndroidPlatform();
            datadogPlatform.Init(options);
            DatadogSdk.InitWithPlatform(datadogPlatform);
        }
    }

    public class DatadogAndroidPlatform : IDatadogPlatform
    {
        public void Init(DatadogConfigurationOptions options)
        {
            var datadogClass = new AndroidJavaClass("com.datadog.android.Datadog");

            using var credentials = new AndroidJavaObject(
                "com.datadog.android.core.configuration.Credentials",
                options.ClientToken,
                "prod",
                string.Empty,     // variant
                null,   // rumApplicationId
                null); // serviceName
            using var configBuilder = new AndroidJavaObject("com.datadog.android.core.configuration.Configuration$Builder", true, false, false, false);
            configBuilder.Call<AndroidJavaObject>("useSite", DatadogConfigurationHelpers.GetSite(options.Site));
            configBuilder.Call<AndroidJavaObject>("setBatchSize", DatadogConfigurationHelpers.GetBatchSize(options.BatchSize));
            configBuilder.Call<AndroidJavaObject>("setUploadFrequency", DatadogConfigurationHelpers.GetUploadFrequency(options.UploadFrequency));

            if (options.CustomEndpoint != string.Empty)
            {
                configBuilder.Call<AndroidJavaObject>("useCustomLogsEndpoint", options.CustomEndpoint);
                configBuilder.Call<AndroidJavaObject>("useCustomCrashReportsEndpoint", options.CustomEndpoint + "/logs");
                configBuilder.Call<AndroidJavaObject>("useCustomRumEndpoint", options.CustomEndpoint + "/rum");
            }

            var configuration = configBuilder.Call<AndroidJavaObject>("build");

            datadogClass.CallStatic(
                "initialize",
                GetApplicationContext(),
                credentials,
                configuration,
                DatadogConfigurationHelpers.GetTrackingConsent(TrackingConsent.Granted));
            datadogClass.CallStatic("setVerbosity", (int)AndroidLogLevel.Verbose);
        }

        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            // TODO:
        }

        public IDdLogger CreateLogger(DatadogLoggingOptions options)
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
            return new DatadogAndroidLogger(androidLogger);
        }

        private AndroidJavaObject GetApplicationContext()
        {
            using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            return currentActivity.Call<AndroidJavaObject>("getApplicationContext");
        }
    }
}