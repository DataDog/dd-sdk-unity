// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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
            var obj = new JObject
            {
                ["timestamp"] = Timestamp,
                ["flag"] = new JObject { ["key"] = FlagKey },
                ["allocation"] = new JObject { ["key"] = AllocationKey },
                ["variant"] = new JObject { ["key"] = VariationKey },
                ["subject"] = new JObject
                {
                    ["id"] = SubjectId,
                    ["attributes"] = JObject.FromObject(SubjectAttributes),
                },
            };
            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
