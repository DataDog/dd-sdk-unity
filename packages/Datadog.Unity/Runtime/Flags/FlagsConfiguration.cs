// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;

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

        /// <summary>
        /// Timeout for each precomputed assignment request, in seconds. The timeout covers
        /// receiving the complete response body. Set to 0 to disable; negative values are
        /// normalized to 0 and values above 2,147,483 are capped. Default: 0.
        /// </summary>
        public readonly int AssignmentRequestTimeoutSeconds;

        /// <summary>
        /// Number of retries after the initial precomputed assignment request fails transiently.
        /// Values are clamped to the range [0, 10]. Set to 0 to disable. Default: 0.
        /// </summary>
        public readonly int AssignmentRequestRetryCount;

        /// <summary>
        /// Fully configured transport used only for assignment requests. When non-null, this
        /// transport is used verbatim and the scalar assignment timeout and retry settings are
        /// not applied. The caller retains ownership of the transport.
        /// </summary>
        public readonly IAssignmentRequestTransport AssignmentRequestTransport;

        public FlagsConfiguration(
            bool trackExposures = true,
            bool trackEvaluations = true,
            float evaluationFlushIntervalSeconds = 10.0f,
            string customFlagsEndpoint = null,
            string customExposureEndpoint = null,
            string customEvaluationEndpoint = null)
            : this(
                assignmentRequestTimeoutSeconds: 0,
                assignmentRequestRetryCount: AssignmentRequestRetryPolicy.DefaultRetryCount,
                trackExposures: trackExposures,
                trackEvaluations: trackEvaluations,
                evaluationFlushIntervalSeconds: evaluationFlushIntervalSeconds,
                customFlagsEndpoint: customFlagsEndpoint,
                customExposureEndpoint: customExposureEndpoint,
                customEvaluationEndpoint: customEvaluationEndpoint,
                assignmentRequestTransport: null)
        {
        }

        public FlagsConfiguration(
            int assignmentRequestTimeoutSeconds,
            int assignmentRequestRetryCount,
            bool trackExposures = true,
            bool trackEvaluations = true,
            float evaluationFlushIntervalSeconds = 10.0f,
            string customFlagsEndpoint = null,
            string customExposureEndpoint = null,
            string customEvaluationEndpoint = null)
            : this(
                assignmentRequestTimeoutSeconds,
                assignmentRequestRetryCount,
                trackExposures,
                trackEvaluations,
                evaluationFlushIntervalSeconds,
                customFlagsEndpoint,
                customExposureEndpoint,
                customEvaluationEndpoint,
                assignmentRequestTransport: null)
        {
        }

        /// <summary>
        /// Creates a Flags configuration with a fully composed assignment-only transport.
        /// The scalar convenience policy is not added to this transport.
        /// </summary>
        public FlagsConfiguration(
            IAssignmentRequestTransport assignmentRequestTransport,
            bool trackExposures = true,
            bool trackEvaluations = true,
            float evaluationFlushIntervalSeconds = 10.0f,
            string customFlagsEndpoint = null,
            string customExposureEndpoint = null,
            string customEvaluationEndpoint = null)
            : this(
                assignmentRequestTimeoutSeconds: 0,
                assignmentRequestRetryCount: AssignmentRequestRetryPolicy.DefaultRetryCount,
                trackExposures: trackExposures,
                trackEvaluations: trackEvaluations,
                evaluationFlushIntervalSeconds: evaluationFlushIntervalSeconds,
                customFlagsEndpoint: customFlagsEndpoint,
                customExposureEndpoint: customExposureEndpoint,
                customEvaluationEndpoint: customEvaluationEndpoint,
                assignmentRequestTransport: assignmentRequestTransport ??
                    throw new ArgumentNullException(nameof(assignmentRequestTransport)))
        {
        }

        private FlagsConfiguration(
            int assignmentRequestTimeoutSeconds,
            int assignmentRequestRetryCount,
            bool trackExposures,
            bool trackEvaluations,
            float evaluationFlushIntervalSeconds,
            string customFlagsEndpoint,
            string customExposureEndpoint,
            string customEvaluationEndpoint,
            IAssignmentRequestTransport assignmentRequestTransport)
        {
            TrackExposures = trackExposures;
            TrackEvaluations = trackEvaluations;
            EvaluationFlushIntervalSeconds = evaluationFlushIntervalSeconds;
            CustomFlagsEndpoint = customFlagsEndpoint;
            CustomExposureEndpoint = customExposureEndpoint;
            CustomEvaluationEndpoint = customEvaluationEndpoint;
            AssignmentRequestTimeoutSeconds = AssignmentRequestRetryPolicy.NormalizeTimeoutSeconds(
                assignmentRequestTimeoutSeconds);
            AssignmentRequestRetryCount = AssignmentRequestRetryPolicy.NormalizeRetryCount(
                assignmentRequestRetryCount);
            AssignmentRequestTransport = assignmentRequestTransport;
        }
    }
}
