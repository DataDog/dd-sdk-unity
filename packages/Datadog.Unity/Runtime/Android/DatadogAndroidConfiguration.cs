// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.
using UnityEngine;

namespace Datadog.Unity.Android
{
    internal static class DatadogConfigurationHelpers
    {
        public static AndroidJavaObject GetSite(DatadogSite site)
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

        public static AndroidJavaObject GetUploadFrequency(UploadFrequency frequency)
        {
            string frequencyName = "AVERAGE";
            switch (frequency)
            {
                case UploadFrequency.Frequent: frequencyName = "FREQUENT"; break;
                case UploadFrequency.Average: frequencyName = "AVERAGE"; break;
                case UploadFrequency.Rare: frequencyName = "RARE"; break;
            }
            using (var uploadFrequencyClass = new AndroidJavaClass("com.datadog.android.core.configuration.UploadFrequency"))
            {
                return uploadFrequencyClass.GetStatic<AndroidJavaObject>(frequencyName);
            }
        }

        public static AndroidJavaObject GetBatchSize(BatchSize size)
        {
            string sizeName = "AVERAGE";
            switch (size)
            {
                case BatchSize.Small: sizeName = "SMALL"; break;
                case BatchSize.Medium: sizeName = "MEDIUM"; break;
                case BatchSize.Large: sizeName = "LARGE"; break;
            }
            using (var uploadFrequencyClass = new AndroidJavaClass("com.datadog.android.core.configuration.BatchSize"))
            {
                return uploadFrequencyClass.GetStatic<AndroidJavaObject>(sizeName);
            }
        }

        public static AndroidJavaObject GetTrackingConsent(TrackingConsent consent)
        {
            string consentName = "AVERAGE";
            switch (consent)
            {
                case TrackingConsent.Granted: consentName = "GRANTED"; break;
                case TrackingConsent.NotGranted: consentName = "NOT_GRANTED"; break;
                case TrackingConsent.Pending: consentName = "PENDING"; break;
            }
            using (var trackingContentClass = new AndroidJavaClass("com.datadog.android.privacy.TrackingConsent"))
            {
                return trackingContentClass.GetStatic<AndroidJavaObject>(consentName);
            }
        }


    }

}
