// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Represents an aggregated flag evaluation event sent to /api/v2/flagevaluation.
    /// </summary>
    internal class FlagEvaluationEvent
    {
        public readonly long Timestamp;
        public readonly string FlagKey;
        public readonly long FirstEvaluation;
        public readonly long LastEvaluation;
        public readonly int EvaluationCount;
        public readonly string VariantKey;
        public readonly string AllocationKey;
        public readonly string TargetingRuleKey;
        public readonly string TargetingKey;
        public readonly bool? RuntimeDefaultUsed;
        public readonly string ErrorMessage;
        public readonly IReadOnlyDictionary<string, object> EvaluationAttributes;

        public FlagEvaluationEvent(
            long timestamp,
            string flagKey,
            long firstEvaluation,
            long lastEvaluation,
            int evaluationCount,
            string variantKey,
            string allocationKey,
            string targetingRuleKey,
            string targetingKey,
            bool? runtimeDefaultUsed,
            string errorMessage,
            IReadOnlyDictionary<string, object> evaluationAttributes)
        {
            Timestamp = timestamp;
            FlagKey = flagKey;
            FirstEvaluation = firstEvaluation;
            LastEvaluation = lastEvaluation;
            EvaluationCount = evaluationCount;
            VariantKey = variantKey;
            AllocationKey = allocationKey;
            TargetingRuleKey = targetingRuleKey;
            TargetingKey = targetingKey;
            RuntimeDefaultUsed = runtimeDefaultUsed;
            ErrorMessage = errorMessage;
            EvaluationAttributes = evaluationAttributes;
        }

        public string ToJson()
        {
            var dto = new FlagEvaluationDto
            {
                Timestamp = Timestamp,
                Flag = new KeyDto { Key = FlagKey },
                FirstEvaluation = FirstEvaluation,
                LastEvaluation = LastEvaluation,
                EvaluationCount = EvaluationCount,
                Variant = (RuntimeDefaultUsed != true && VariantKey != null) ? new KeyDto { Key = VariantKey } : null,
                Allocation = (RuntimeDefaultUsed != true && AllocationKey != null) ? new KeyDto { Key = AllocationKey } : null,
                TargetingRule = TargetingRuleKey != null ? new KeyDto { Key = TargetingRuleKey } : null,
                TargetingKey = TargetingKey,
                RuntimeDefaultUsed = RuntimeDefaultUsed,
                Error = ErrorMessage != null ? new ErrorDto { Message = ErrorMessage } : null,
                Context = (EvaluationAttributes != null && EvaluationAttributes.Count > 0)
                    ? new ContextDto { Evaluation = EvaluationAttributes }
                    : null,
            };
            return JsonConvert.SerializeObject(dto, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
            });
        }

        private class FlagEvaluationDto
        {
            [JsonProperty("timestamp")]
            public long Timestamp { get; set; }

            [JsonProperty("flag")]
            public KeyDto Flag { get; set; }

            [JsonProperty("first_evaluation")]
            public long FirstEvaluation { get; set; }

            [JsonProperty("last_evaluation")]
            public long LastEvaluation { get; set; }

            [JsonProperty("evaluation_count")]
            public int EvaluationCount { get; set; }

            [JsonProperty("variant")]
            public KeyDto Variant { get; set; }

            [JsonProperty("allocation")]
            public KeyDto Allocation { get; set; }

            [JsonProperty("targeting_rule")]
            public KeyDto TargetingRule { get; set; }

            [JsonProperty("targeting_key")]
            public string TargetingKey { get; set; }

            [JsonProperty("runtime_default_used")]
            public bool? RuntimeDefaultUsed { get; set; }

            [JsonProperty("error")]
            public ErrorDto Error { get; set; }

            [JsonProperty("context")]
            public ContextDto Context { get; set; }
        }

        private class KeyDto
        {
            [JsonProperty("key")]
            public string Key { get; set; }
        }

        private class ErrorDto
        {
            [JsonProperty("message")]
            public string Message { get; set; }
        }

        private class ContextDto
        {
            [JsonProperty("evaluation")]
            public IReadOnlyDictionary<string, object> Evaluation { get; set; }
        }
    }
}
