// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Numerics;
using Newtonsoft.Json;
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
            string reason,
            long? serialId = null)
        {
            VariationType = variationType ?? string.Empty;
            VariationValue = variationValue;
            DoLog = doLog;
            AllocationKey = allocationKey;
            VariationKey = variationKey ?? string.Empty;
            Reason = reason ?? string.Empty;
            SerialId = serialId;
        }

        /// <summary>
        /// Gets the type of the variation value (boolean, string, integer, number, float, object).
        /// </summary>
        public string VariationType { get; }

        public long? SerialId { get; }

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

        private bool IsNumericType() =>
            VariationType == "integer" || VariationType == "number" || VariationType == "float";

        private bool IsCompatible(Type type)
        {
            var tokenType = VariationValue.Type;
            if (type == typeof(bool))
                return VariationType == "boolean" && tokenType == JTokenType.Boolean;
            if (type == typeof(string))
                return VariationType == "string" && tokenType == JTokenType.String;
            if (type == typeof(int))
            {
                if (!IsNumericType()) return false;
                if (tokenType != JTokenType.Integer && tokenType != JTokenType.Float) return false;
                var number = VariationValue.Value<double>();
                return !double.IsNaN(number) && !double.IsInfinity(number)
                    && number >= int.MinValue && number <= int.MaxValue
                    && Math.Truncate(number) == number;
            }
            if (type == typeof(double))
            {
                if (!IsNumericType() || (tokenType != JTokenType.Integer && tokenType != JTokenType.Float))
                    return false;
                var number = VariationValue.Value<double>();
                if (double.IsNaN(number) || double.IsInfinity(number)) return false;
                return tokenType != JTokenType.Integer ||
                    (BigInteger.TryParse(VariationValue.ToString(Formatting.None), out var integer) &&
                        integer == new BigInteger(number));
            }
            return VariationType == "object" && tokenType == JTokenType.Object;
        }

        /// <summary>
        /// Attempts to get the variation value as the specified type.
        /// </summary>
        /// <param name="value">The converted value, or default on failure.</param>
        /// <param name="flagKey">The flag key, used in the warning log on type mismatch.</param>
        /// <param name="logger">Optional logger; receives a warning when conversion fails.</param>
        public bool TryGetValue<T>(out T value, string flagKey = null, IInternalLogger logger = null)
        {
            if (VariationValue == null || VariationValue.Type == JTokenType.Null || !IsCompatible(typeof(T)))
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
