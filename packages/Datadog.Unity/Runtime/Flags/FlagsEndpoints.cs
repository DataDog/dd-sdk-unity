// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Maps DatadogSite to the correct precompute and intake endpoints.
    /// </summary>
    internal static class FlagsEndpoints
    {
        /// <summary>
        /// Gets the precompute assignments CDN endpoint for the given site.
        /// </summary>
        public static string GetPrecomputeEndpoint(DatadogSite site)
        {
            switch (site)
            {
                case DatadogSite.Us1:
                    return "https://preview.ff-cdn.datadoghq.com/precompute-assignments";
                case DatadogSite.Us3:
                    return "https://preview.ff-cdn.us3.datadoghq.com/precompute-assignments";
                case DatadogSite.Us5:
                    return "https://preview.ff-cdn.us5.datadoghq.com/precompute-assignments";
                case DatadogSite.Eu1:
                    return "https://preview.ff-cdn.datadoghq.eu/precompute-assignments";
                case DatadogSite.Ap1:
                    return "https://preview.ff-cdn.ap1.datadoghq.com/precompute-assignments";
                case DatadogSite.Ap2:
                    return "https://preview.ff-cdn.ap2.datadoghq.com/precompute-assignments";
                case DatadogSite.Us1Fed:
                    return "https://preview.ff-cdn.ddog-gov.com/precompute-assignments";
                default:
                    return "https://preview.ff-cdn.datadoghq.com/precompute-assignments";
            }
        }

        /// <summary>
        /// Gets the intake endpoint base URL for the given site.
        /// </summary>
        public static string GetIntakeEndpoint(DatadogSite site)
        {
            switch (site)
            {
                case DatadogSite.Us1:
                    return "https://browser-intake-datadoghq.com";
                case DatadogSite.Us3:
                    return "https://browser-intake-us3-datadoghq.com";
                case DatadogSite.Us5:
                    return "https://browser-intake-us5-datadoghq.com";
                case DatadogSite.Eu1:
                    return "https://browser-intake-datadoghq.eu";
                case DatadogSite.Ap1:
                    return "https://browser-intake-ap1-datadoghq.com";
                case DatadogSite.Ap2:
                    return "https://browser-intake-ap2-datadoghq.com";
                case DatadogSite.Us1Fed:
                    return "https://browser-intake-ddog-gov.com";
                default:
                    return "https://browser-intake-datadoghq.com";
            }
        }

        /// <summary>
        /// Gets the exposure events endpoint URL.
        /// </summary>
        public static string GetExposureEndpoint(DatadogSite site, string customEndpoint = null)
        {
            if (!string.IsNullOrEmpty(customEndpoint))
            {
                return customEndpoint;
            }
            return GetIntakeEndpoint(site) + "/api/v2/exposures";
        }

        /// <summary>
        /// Gets the flag evaluation events endpoint URL.
        /// </summary>
        public static string GetEvaluationEndpoint(DatadogSite site, string customEndpoint = null)
        {
            if (!string.IsNullOrEmpty(customEndpoint))
            {
                return customEndpoint;
            }
            return GetIntakeEndpoint(site) + "/api/v2/flagevaluation";
        }
    }
}
