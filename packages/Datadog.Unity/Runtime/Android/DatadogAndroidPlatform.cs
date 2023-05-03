// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using UnityEngine;
using UnityEngine.Scripting;

[assembly: UnityEngine.Scripting.Preserve]
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Datadog.Unity.Android
{
    // These are mappings to android.util.Log
    internal enum AndroidLogLevels
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
            datadogClass.CallStatic("setVerbosity", (int)AndroidLogLevels.Verbose);

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

            var configuration = configBuilder.Call<AndroidJavaObject>("build");

            datadogClass.CallStatic(
                "initialize",
                GetApplicationContext(),
                credentials,
                configuration,
                DatadogConfigurationHelpers.GetTrackingConsent(TrackingConsent.Granted));
        }

        public IDdLogger CreateLogger()
        {
            using var loggerBuilder = new AndroidJavaObject("com.datadog.android.log.Logger$Builder");
            var androidLoger = loggerBuilder.Call<AndroidJavaObject>("build");
            return new DatadogAndroidLogger(androidLoger);
        }

        private AndroidJavaObject GetApplicationContext()
        {
            using AndroidJavaClass unityPlayer = new ("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            return currentActivity.Call<AndroidJavaObject>("getApplicationContext");
        }
    }
}
