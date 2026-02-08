// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Text;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Represents an exposure event sent to the /api/v2/exposures endpoint.
    /// </summary>
    internal class ExposureEvent
    {
        public ExposureEvent(long timestamp, string flagKey, string allocationKey, string variationKey, string subjectId, Dictionary<string, object> subjectAttributes)
        {
            Timestamp = timestamp;
            FlagKey = flagKey;
            AllocationKey = allocationKey;
            VariationKey = variationKey;
            SubjectId = subjectId;
            SubjectAttributes = subjectAttributes ?? new Dictionary<string, object>();
        }

        public long Timestamp { get; }
        public string FlagKey { get; }
        public string AllocationKey { get; }
        public string VariationKey { get; }
        public string SubjectId { get; }
        public Dictionary<string, object> SubjectAttributes { get; }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.AppendFormat("\"timestamp\":{0}", Timestamp);
            sb.AppendFormat(",\"flag\":{{\"key\":{0}}}", JsonHelper.Escape(FlagKey));
            sb.AppendFormat(",\"allocation\":{{\"key\":{0}}}", JsonHelper.Escape(AllocationKey));
            sb.AppendFormat(",\"variant\":{{\"key\":{0}}}", JsonHelper.Escape(VariationKey));
            sb.Append(",\"subject\":{");
            sb.AppendFormat("\"id\":{0}", JsonHelper.Escape(SubjectId));
            sb.Append(",\"attributes\":");
            sb.Append(JsonHelper.DictionaryToJson(SubjectAttributes));
            sb.Append('}');
            sb.Append('}');
            return sb.ToString();
        }
    }
}
