// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Represents a precomputed flag assignment returned from the server.
    /// </summary>
    internal class FlagAssignment
    {
        public FlagAssignment(
            string variationType,
            JToken variationValue,
            bool doLog,
            string allocationKey,
            string variationKey,
            string reason)
        {
            VariationType = variationType ?? string.Empty;
            VariationValue = variationValue;
            DoLog = doLog;
            AllocationKey = allocationKey ?? string.Empty;
            VariationKey = variationKey ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        /// <summary>
        /// Gets the type of the variation value (boolean, string, integer, number, float, object).
        /// </summary>
        public string VariationType { get; }

        /// <summary>
        /// Gets the raw variation value token.
        /// </summary>
        public JToken VariationValue { get; }

        /// <summary>
        /// Gets whether to track exposure for this flag.
        /// </summary>
        public bool DoLog { get; }

        /// <summary>
        /// Gets the allocation identifier.
        /// </summary>
        public string AllocationKey { get; }

        /// <summary>
        /// Gets the variation identifier.
        /// </summary>
        public string VariationKey { get; }

        /// <summary>
        /// Gets the resolution reason (DEFAULT, TARGETING_MATCH, RULE_MATCH, etc.).
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Attempts to get the variation value as the specified type.
        /// </summary>
        /// <param name="value">The converted value, or default on failure.</param>
        /// <param name="flagKey">The flag key, used in the warning log on type mismatch.</param>
        /// <param name="logger">Optional logger; receives a warning when conversion fails.</param>
        public bool TryGetValue<T>(out T value, string flagKey = null, IInternalLogger logger = null)
        {
            if (VariationValue == null || VariationValue.Type == JTokenType.Null)
            {
                value = default;
                return false;
            }

            try
            {
                value = VariationValue.ToObject<T>();
                return true;
            }
            catch (Exception e)
            {
                logger?.Log(DdLogLevel.Warn,
                    $"Flag '{flagKey}': could not convert value to {typeof(T).Name}: {e.Message}");
                value = default;
                return false;
            }
        }
    }
}
