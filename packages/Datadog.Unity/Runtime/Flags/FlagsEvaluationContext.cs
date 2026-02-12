// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Represents the evaluation context used for feature flag evaluation.
    /// Contains the targeting key and optional attributes for targeting rules.
    /// </summary>
    public class FlagsEvaluationContext
    {
        private readonly Dictionary<string, object> _attributes;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlagsEvaluationContext"/> class.
        /// </summary>
        /// <param name="targetingKey">The unique identifier for targeting/bucketing (e.g. user ID).</param>
        /// <param name="attributes">Optional custom attributes for targeting rules.</param>
        public FlagsEvaluationContext(string targetingKey, Dictionary<string, object> attributes = null)
        {
            TargetingKey = targetingKey ?? string.Empty;
            _attributes = attributes != null
                ? new Dictionary<string, object>(attributes)
                : new Dictionary<string, object>();
        }

        /// <summary>
        /// Gets the unique identifier used for targeting and bucketing.
        /// </summary>
        public string TargetingKey { get; }

        /// <summary>
        /// Gets the custom attributes used for targeting rules.
        /// </summary>
        public IReadOnlyDictionary<string, object> Attributes => _attributes;
    }
}
