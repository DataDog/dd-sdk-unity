// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using UnityEngine;
using UnityEngine.Scripting;

[assembly: UnityEngine.Scripting.Preserve]
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
namespace Datadog.Unity.Android
{

    public class DatadogAndroidPlatform : IDatadogPlatform
    {
        public void Init(DatadogConfigurationOptions options)
        {
            var datadogClass = new AndroidJavaClass("com.datadog.android.Datadog");
            datadogClass.CallStatic("setVerbosity", 2);

            using (var credentials = new AndroidJavaObject("com.datadog.android.core.configuration.Credentials",
                options.ClientToken,
                "prod",
                "",     // variant
                null,   // rumApplicaitonId
                null    // serviceName
                ))
            using (var configBuilder = new AndroidJavaObject("com.datadog.android.core.configuration.Configuration$Builder", true, false, false, false))
            {
                configBuilder.Call<AndroidJavaObject>("useSite", GetSite(options.Site));
                configBuilder.Call<AndroidJavaObject>("setBatchSize", GetBatchSize());
                configBuilder.Call<AndroidJavaObject>("setUploadFrequency", GetUploadFrequency());

                var configuration = configBuilder.Call<AndroidJavaObject>("build");

                datadogClass.CallStatic("initialize", GetApplicationContext(), credentials, configuration, GetTrackingConsent());
            }
        }

        public IDdLogger CreateLogger()
        {
            using (var loggerBuilder = new AndroidJavaObject("com.datadog.android.log.Logger$Builder"))
            {
                var androidLoger = loggerBuilder.Call<AndroidJavaObject>("build");
                return new DatadogAndroidLogger(androidLoger);
            }
        }

        private AndroidJavaObject GetSite(DatadogSite site)
        {
            string siteName = "US1";
            switch (site)
            {
                case DatadogSite.us1: siteName = "US1"; break;
                case DatadogSite.us3: siteName = "US3"; break;
                case DatadogSite.us5: siteName = "US5"; break;
                case DatadogSite.eu1: siteName = "EU1"; break;
                case DatadogSite.ap1: siteName = "AP1"; break;
                case DatadogSite.us1Fed: siteName = "US1_FED"; break;
            }
            using (var siteClass = new AndroidJavaClass("com.datadog.android.DatadogSite"))
            {
                return siteClass.GetStatic<AndroidJavaObject>(siteName);
            }
        }

        private AndroidJavaObject GetUploadFrequency()
        {
            using (var uploadFrequencyClass = new AndroidJavaClass("com.datadog.android.core.configuration.UploadFrequency"))
            {
                return uploadFrequencyClass.GetStatic<AndroidJavaObject>("FREQUENT");
            }
        }

        private AndroidJavaObject GetBatchSize()
        {
            using (var uploadFrequencyClass = new AndroidJavaClass("com.datadog.android.core.configuration.BatchSize"))
            {
                return uploadFrequencyClass.GetStatic<AndroidJavaObject>("SMALL");
            }
        }

        private AndroidJavaObject GetTrackingConsent()
        {
            using (var trackingContentClass = new AndroidJavaClass("com.datadog.android.privacy.TrackingConsent"))
            {
                return trackingContentClass.GetStatic<AndroidJavaObject>("GRANTED");
            }
        }

        private AndroidJavaObject GetApplicationContext()
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                return currentActivity.Call<AndroidJavaObject>("getApplicationContext");
            }
        }
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
}