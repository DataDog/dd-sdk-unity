// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.
using Datadog.Unity.Logs;
using UnityEngine;

namespace Datadog.Unity.Android
{
    internal static class DatadogConfigurationHelpers
    {
        internal static AndroidLogLevel DdLogLevelToAndroidLogLevel(DdLogLevel logLevel)
        {
            return logLevel switch
            {
                DdLogLevel.Debug => AndroidLogLevel.Debug,
                DdLogLevel.Info => AndroidLogLevel.Info,
                DdLogLevel.Notice => AndroidLogLevel.Info,
                DdLogLevel.Warn => AndroidLogLevel.Warn,
                DdLogLevel.Error => AndroidLogLevel.Error,
                DdLogLevel.Critical => AndroidLogLevel.Assert,
                _ => AndroidLogLevel.Debug,
            };
        }

        internal static AndroidJavaObject GetSite(DatadogSite site)
        {
            string siteName = site switch
            {
                DatadogSite.Us1 => "US1",
                DatadogSite.Us3 => "US3",
                DatadogSite.Us5 => "US5",
                DatadogSite.Eu1 => "EU1",
                DatadogSite.Us1Fed => "US1_FED",
                DatadogSite.Ap1 => "AP1",
                _ => "US1",
            };

            using var siteClass = new AndroidJavaClass("com.datadog.android.DatadogSite");
            return siteClass.GetStatic<AndroidJavaObject>(siteName);
        }

        internal static AndroidJavaObject GetUploadFrequency(UploadFrequency frequency)
        {
            string frequencyName = frequency switch
            {
                UploadFrequency.Frequent => "FREQUENT",
                UploadFrequency.Average => "AVERAGE",
                UploadFrequency.Rare => "RARE",
                _ => "AVERAGE",
            };

            using var uploadFrequencyClass = new AndroidJavaClass("com.datadog.android.core.configuration.UploadFrequency");
            return uploadFrequencyClass.GetStatic<AndroidJavaObject>(frequencyName);
        }

        internal static AndroidJavaObject GetBatchSize(BatchSize size)
        {
            string sizeName = size switch
            {
                BatchSize.Small => "SMALL",
                BatchSize.Medium => "MEDIUM",
                BatchSize.Large => "LARGE",
                _ => "MEDIUM"
            };
            using var uploadFrequencyClass = new AndroidJavaClass("com.datadog.android.core.configuration.BatchSize");
            return uploadFrequencyClass.GetStatic<AndroidJavaObject>(sizeName);
        }

        internal static AndroidJavaObject GetTrackingConsent(TrackingConsent consent)
        {
            string consentName = consent switch
            {
                TrackingConsent.Granted => "GRANTED",
                TrackingConsent.NotGranted => "NOT_GRANTED",
                TrackingConsent.Pending => "PENDING",
                _ => "PENDING"
            };
            using var trackingContentClass = new AndroidJavaClass("com.datadog.android.privacy.TrackingConsent");
            return trackingContentClass.GetStatic<AndroidJavaObject>(consentName);
        }
    }
}
