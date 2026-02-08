// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Represents a precomputed flag assignment returned from the server.
    /// </summary>
    internal class FlagAssignment
    {
        public FlagAssignment(
            string variationType,
            object variationValue,
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
        /// Gets the parsed variation value.
        /// </summary>
        public object VariationValue { get; }

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
        public bool TryGetValue<T>(out T value)
        {
            try
            {
                if (VariationValue is T typedValue)
                {
                    value = typedValue;
                    return true;
                }

                // Handle numeric conversions
                if (typeof(T) == typeof(int) && VariationValue is long longVal)
                {
                    value = (T)(object)(int)longVal;
                    return true;
                }

                if (typeof(T) == typeof(double) && VariationValue is long longVal2)
                {
                    value = (T)(object)(double)longVal2;
                    return true;
                }

                if (typeof(T) == typeof(double) && VariationValue is int intVal)
                {
                    value = (T)(object)(double)intVal;
                    return true;
                }

                if (typeof(T) == typeof(int) && VariationValue is double doubleVal)
                {
                    value = (T)(object)(int)doubleVal;
                    return true;
                }

                value = default;
                return false;
            }
            catch
            {
                value = default;
                return false;
            }
        }
    }
}
