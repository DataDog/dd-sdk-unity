// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Datadog.Unity.Logs;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Represents the evaluation context used for feature flag evaluation.
    /// Contains the targeting key and optional attributes for targeting rules.
    ///
    /// Attributes are stored as strings. Nested objects are flattened with dot notation:
    /// <c>{ "address": { "city": "NY" } }</c> becomes <c>{ "address.city": "NY" }</c>.
    /// This matches what the precomputed assignments endpoint expects and ensures the
    /// context is fully immutable.
    /// </summary>
    public class FlagsEvaluationContext
    {
        /// <summary>
        /// Gets the unique identifier used for targeting and bucketing.
        /// </summary>
        public readonly string TargetingKey;

        /// <summary>
        /// Gets the custom attributes used for targeting rules, flattened to string values.
        /// </summary>
        public readonly IReadOnlyDictionary<string, string> Attributes;

        /// <summary>
        /// Initializes a new instance of the <see cref="FlagsEvaluationContext"/> class.
        /// </summary>
        /// <param name="targetingKey">The unique identifier for targeting/bucketing (e.g. user ID).</param>
        /// <param name="attributes">Optional custom attributes for targeting rules. Nested objects
        /// are flattened using dot notation; all values are converted to strings.</param>
        public FlagsEvaluationContext(string targetingKey, Dictionary<string, object> attributes = null)
        {
            TargetingKey = targetingKey ?? string.Empty;

            if (string.IsNullOrEmpty(TargetingKey))
            {
                DatadogSdk.Instance?.InternalLogger?.Log(DdLogLevel.Warn,
                    "FlagsEvaluationContext created with a null or empty targeting key. " +
                    "The targeting key is the bucketing key for flag assignments and must be a stable, " +
                    "unique identifier per user (e.g. user ID). An empty key will produce a 400 " +
                    "from the assignments endpoint.");
            }

            // Always initialize Attributes — never null, even when no attributes are provided.
            var flat = new Dictionary<string, string>();
            if (attributes != null)
            {
                Flatten(attributes, prefix: null, flat);
            }
            Attributes = new ReadOnlyDictionary<string, string>(flat);
        }

        private static void Flatten(Dictionary<string, object> source, string prefix, Dictionary<string, string> result)
        {
            foreach (var kvp in source)
            {
                var key = prefix != null ? prefix + "." + kvp.Key : kvp.Key;
                if (kvp.Value is Dictionary<string, object> nested)
                {
                    Flatten(nested, key, result);
                }
                else
                {
                    result[key] = kvp.Value?.ToString() ?? string.Empty;
                }
            }
        }
    }
}
