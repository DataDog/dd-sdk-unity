// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Represents an aggregated flag evaluation event sent to /api/v2/flagevaluation.
    /// </summary>
    internal class FlagEvaluationEvent
    {
        public long Timestamp { get; set; }
        public string FlagKey { get; set; }
        public long FirstEvaluation { get; set; }
        public long LastEvaluation { get; set; }
        public int EvaluationCount { get; set; }
        public string VariantKey { get; set; }
        public string AllocationKey { get; set; }
        public string TargetingRuleKey { get; set; }
        public string TargetingKey { get; set; }
        public bool? RuntimeDefaultUsed { get; set; }
        public string ErrorMessage { get; set; }
        public IReadOnlyDictionary<string, object> EvaluationAttributes { get; set; }

        public string ToJson()
        {
            var obj = new JObject
            {
                ["timestamp"] = Timestamp,
                ["flag"] = new JObject { ["key"] = FlagKey },
                ["first_evaluation"] = FirstEvaluation,
                ["last_evaluation"] = LastEvaluation,
                ["evaluation_count"] = EvaluationCount,
            };

            if (RuntimeDefaultUsed != true && VariantKey != null)
            {
                obj["variant"] = new JObject { ["key"] = VariantKey };
            }

            if (RuntimeDefaultUsed != true && AllocationKey != null)
            {
                obj["allocation"] = new JObject { ["key"] = AllocationKey };
            }

            if (TargetingRuleKey != null)
            {
                obj["targeting_rule"] = new JObject { ["key"] = TargetingRuleKey };
            }

            if (TargetingKey != null)
            {
                obj["targeting_key"] = TargetingKey;
            }

            if (RuntimeDefaultUsed.HasValue)
            {
                obj["runtime_default_used"] = RuntimeDefaultUsed.Value;
            }

            if (ErrorMessage != null)
            {
                obj["error"] = new JObject { ["message"] = ErrorMessage };
            }

            if (EvaluationAttributes != null && EvaluationAttributes.Count > 0)
            {
                obj["context"] = new JObject
                {
                    ["evaluation"] = JObject.FromObject(EvaluationAttributes),
                };
            }

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
