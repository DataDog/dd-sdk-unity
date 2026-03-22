// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Configuration options for the Datadog Flags feature.
    /// </summary>
    public class FlagsConfiguration
    {
        /// <summary>
        /// Enables exposure logging via the dedicated exposures intake endpoint.
        /// Default: true.
        /// </summary>
        public bool TrackExposures { get; set; } = true;

        /// <summary>
        /// Enables evaluation logging via the dedicated evaluations intake endpoint.
        /// Default: true.
        /// </summary>
        public bool TrackEvaluations { get; set; } = true;

        /// <summary>
        /// The interval in seconds at which aggregated evaluation data is flushed.
        /// Clamped to [1, 60]. Default: 10.
        /// </summary>
        public float EvaluationFlushIntervalSeconds { get; set; } = 10.0f;

        /// <summary>
        /// Custom server URL for retrieving flag assignments.
        /// If null, the SDK uses the default Datadog Flags endpoint for the configured site.
        /// </summary>
        public string CustomFlagsEndpoint { get; set; }

        /// <summary>
        /// Custom server URL for sending exposure events.
        /// </summary>
        public string CustomExposureEndpoint { get; set; }

        /// <summary>
        /// Custom server URL for sending evaluation events.
        /// </summary>
        public string CustomEvaluationEndpoint { get; set; }
    }
}
