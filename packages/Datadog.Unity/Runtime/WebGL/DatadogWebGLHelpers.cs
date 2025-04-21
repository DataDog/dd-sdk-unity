// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.WebGL
{
    internal static class DatadogWebGLHelpers
    {
        internal static string SiteStringForSite(DatadogSite site)
        {
            return site switch
            {
                DatadogSite.Us1 => "datadoghq.com",
                DatadogSite.Us3 => "us3.datadoghq.com",
                DatadogSite.Us5 => "us5.datadoghq.com",
                DatadogSite.Eu1 => "datadoghq.eu",
                DatadogSite.Us1Fed => "ddog-gov.com",
                DatadogSite.Ap1 => "ap1.datadoghq.com",
                _ => "datadoghq.com"
            };
        }
    }
}
