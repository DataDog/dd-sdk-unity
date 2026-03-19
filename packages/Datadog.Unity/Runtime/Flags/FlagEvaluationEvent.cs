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
        public string Key { get; set; }
    }

    internal class FlagErrorDetail
    {
        [JsonProperty("message")]
        public string Message { get; set; }
    }

    internal class EvaluationContextPayload
    {
        [JsonProperty("evaluation")]
        public IReadOnlyDictionary<string, string> Evaluation { get; set; }
    }

    /// <summary>
    /// Aggregated flag evaluation event sent to /api/v2/flagevaluation.
    /// Serialise directly with <c>JsonConvert.SerializeObject</c>.
    /// </summary>
    internal class FlagEvaluationEvent
    {
        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        [JsonProperty("flag")]
        public FlagRef Flag { get; set; }

        [JsonProperty("first_evaluation")]
        public long FirstEvaluation { get; set; }

        [JsonProperty("last_evaluation")]
        public long LastEvaluation { get; set; }

        [JsonProperty("evaluation_count")]
        public int EvaluationCount { get; set; }

        [JsonProperty("variant", NullValueHandling = NullValueHandling.Ignore)]
        public FlagRef Variant { get; set; }

        [JsonProperty("allocation", NullValueHandling = NullValueHandling.Ignore)]
        public FlagRef Allocation { get; set; }

        [JsonProperty("targeting_rule", NullValueHandling = NullValueHandling.Ignore)]
        public FlagRef TargetingRule { get; set; }

        [JsonProperty("targeting_key", NullValueHandling = NullValueHandling.Ignore)]
        public string TargetingKey { get; set; }

        [JsonProperty("runtime_default_used", NullValueHandling = NullValueHandling.Ignore)]
        public bool? RuntimeDefaultUsed { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public FlagErrorDetail Error { get; set; }

        [JsonProperty("context", NullValueHandling = NullValueHandling.Ignore)]
        public EvaluationContextPayload Context { get; set; }
    }
}
