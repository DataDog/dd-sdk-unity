// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Immutable configuration options for the Datadog Flags feature.
    /// All parameters are optional; defaults are used for any omitted values.
    /// </summary>
    public class FlagsConfiguration
    {
        /// <summary>
        /// Enables exposure logging via the dedicated exposures intake endpoint.
        /// Default: true.
        /// </summary>
        public readonly bool TrackExposures;

        /// <summary>
        /// Enables evaluation logging via the dedicated evaluations intake endpoint.
        /// Default: true.
        /// </summary>
        public readonly bool TrackEvaluations;

        /// <summary>
        /// The interval in seconds at which aggregated evaluation data is flushed.
        /// When used by the evaluation aggregator, this value is clamped to [1, 60]. Default: 10.
        /// </summary>
        public readonly float EvaluationFlushIntervalSeconds;

        /// <summary>
        /// Custom server URL for retrieving flag assignments.
        /// If null, the SDK uses the default Datadog Flags endpoint for the configured site.
        /// </summary>
        public readonly string CustomFlagsEndpoint;

        /// <summary>
        /// Custom server URL for sending exposure events.
        /// </summary>
        public readonly string CustomExposureEndpoint;

        /// <summary>
        /// Custom server URL for sending evaluation events.
        /// </summary>
        public readonly string CustomEvaluationEndpoint;

        public FlagsConfiguration(
            bool trackExposures = true,
            bool trackEvaluations = true,
            float evaluationFlushIntervalSeconds = 10.0f,
            string customFlagsEndpoint = null,
            string customExposureEndpoint = null,
            string customEvaluationEndpoint = null)
        {
            TrackExposures = trackExposures;
            TrackEvaluations = trackEvaluations;
            EvaluationFlushIntervalSeconds = evaluationFlushIntervalSeconds;
            CustomFlagsEndpoint = customFlagsEndpoint;
            CustomExposureEndpoint = customExposureEndpoint;
            CustomEvaluationEndpoint = customEvaluationEndpoint;
        }
    }
}
