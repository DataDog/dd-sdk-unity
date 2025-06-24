// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.WebGL
{
    internal static class DatadogWebGLHelpers
    {
        internal static string ToWebValue(this DatadogSite site)
        {
            return site switch
            {
                DatadogSite.Us1 => "datadoghq.com",
                DatadogSite.Us3 => "us3.datadoghq.com",
                DatadogSite.Us5 => "us5.datadoghq.com",
                DatadogSite.Eu1 => "datadoghq.eu",
                DatadogSite.Us1Fed => "ddog-gov.com",
                DatadogSite.Ap1 => "ap1.datadoghq.com",
                DatadogSite.Ap2 => "ap2.datadoghq.com",
                _ => "datadoghq.com"
            };
        }

        internal static string ToWebValue(this TrackingConsent consent)
        {
            return consent switch
            {
                TrackingConsent.NotGranted => "not-granted",
                TrackingConsent.Granted => "granted",
                _ => "not-granted"
            };
        }
    }
}
