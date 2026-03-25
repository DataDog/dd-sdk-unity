// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// A <c>{ "key": "..." }</c> reference used in evaluation and exposure event payloads.
    /// </summary>
    internal class FlagRef
    {
        [JsonProperty("key")]
        public readonly string Key;

        public FlagRef(string key)
        {
            Key = key;
        }
    }

    internal class FlagErrorDetail
    {
        [JsonProperty("message")]
        public readonly string Message;

        public FlagErrorDetail(string message)
        {
            Message = message;
        }
    }

    internal class EvaluationContextPayload
    {
        [JsonProperty("evaluation")]
        public readonly IReadOnlyDictionary<string, string> Evaluation;

        public EvaluationContextPayload(IReadOnlyDictionary<string, string> evaluation)
        {
            Evaluation = evaluation;
        }
    }

    /// <summary>
    /// Aggregated flag evaluation event sent to /api/v2/flagevaluation.
    /// Serialise directly with <c>JsonConvert.SerializeObject</c>.
    /// </summary>
    internal class FlagEvaluationEvent
    {
        [JsonProperty("timestamp")]
        public readonly long Timestamp;

        [JsonProperty("flag")]
        public readonly FlagRef Flag;

        [JsonProperty("first_evaluation")]
        public readonly long FirstEvaluation;

        [JsonProperty("last_evaluation")]
        public readonly long LastEvaluation;

        [JsonProperty("evaluation_count")]
        public readonly int EvaluationCount;

        [JsonProperty("variant", NullValueHandling = NullValueHandling.Ignore)]
        public readonly FlagRef Variant;

        [JsonProperty("allocation", NullValueHandling = NullValueHandling.Ignore)]
        public readonly FlagRef Allocation;

        [JsonProperty("targeting_rule", NullValueHandling = NullValueHandling.Ignore)]
        public readonly FlagRef TargetingRule;

        [JsonProperty("targeting_key", NullValueHandling = NullValueHandling.Ignore)]
        public readonly string TargetingKey;

        [JsonProperty("runtime_default_used", NullValueHandling = NullValueHandling.Ignore)]
        public readonly bool? RuntimeDefaultUsed;

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public readonly FlagErrorDetail Error;

        [JsonProperty("context", NullValueHandling = NullValueHandling.Ignore)]
        public readonly EvaluationContextPayload Context;

        public FlagEvaluationEvent(
            long timestamp,
            FlagRef flag,
            long firstEvaluation,
            long lastEvaluation,
            int evaluationCount,
            FlagRef variant = null,
            FlagRef allocation = null,
            FlagRef targetingRule = null,
            string targetingKey = null,
            bool? runtimeDefaultUsed = null,
            FlagErrorDetail error = null,
            EvaluationContextPayload context = null)
        {
            Timestamp = timestamp;
            Flag = flag;
            FirstEvaluation = firstEvaluation;
            LastEvaluation = lastEvaluation;
            EvaluationCount = evaluationCount;
            Variant = variant;
            Allocation = allocation;
            TargetingRule = targetingRule;
            TargetingKey = targetingKey;
            RuntimeDefaultUsed = runtimeDefaultUsed;
            Error = error;
            Context = context;
        }
    }
}
