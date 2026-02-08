// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Text;

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
        public Dictionary<string, object> EvaluationAttributes { get; set; }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.AppendFormat("\"timestamp\":{0}", Timestamp);
            sb.AppendFormat(",\"flag\":{{\"key\":{0}}}", JsonHelper.Escape(FlagKey));
            sb.AppendFormat(",\"first_evaluation\":{0}", FirstEvaluation);
            sb.AppendFormat(",\"last_evaluation\":{0}", LastEvaluation);
            sb.AppendFormat(",\"evaluation_count\":{0}", EvaluationCount);

            if (RuntimeDefaultUsed != true && VariantKey != null)
            {
                sb.AppendFormat(",\"variant\":{{\"key\":{0}}}", JsonHelper.Escape(VariantKey));
            }

            if (RuntimeDefaultUsed != true && AllocationKey != null)
            {
                sb.AppendFormat(",\"allocation\":{{\"key\":{0}}}", JsonHelper.Escape(AllocationKey));
            }

            if (TargetingRuleKey != null)
            {
                sb.AppendFormat(",\"targeting_rule\":{{\"key\":{0}}}", JsonHelper.Escape(TargetingRuleKey));
            }

            if (TargetingKey != null)
            {
                sb.AppendFormat(",\"targeting_key\":{0}", JsonHelper.Escape(TargetingKey));
            }

            if (RuntimeDefaultUsed.HasValue)
            {
                sb.AppendFormat(",\"runtime_default_used\":{0}", RuntimeDefaultUsed.Value ? "true" : "false");
            }

            if (ErrorMessage != null)
            {
                sb.AppendFormat(",\"error\":{{\"message\":{0}}}", JsonHelper.Escape(ErrorMessage));
            }

            if (EvaluationAttributes != null && EvaluationAttributes.Count > 0)
            {
                sb.Append(",\"context\":{\"evaluation\":");
                sb.Append(JsonHelper.DictionaryToJson(EvaluationAttributes));
                sb.Append('}');
            }

            sb.Append('}');
            return sb.ToString();
        }
    }
}
