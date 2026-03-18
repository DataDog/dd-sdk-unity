// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Represents an exposure event sent to the /api/v2/exposures endpoint.
    /// </summary>
    internal class ExposureEvent
    {
        public readonly long Timestamp;
        public readonly string FlagKey;
        public readonly string AllocationKey;
        public readonly string VariationKey;
        public readonly string SubjectId;
        public readonly IReadOnlyDictionary<string, object> SubjectAttributes;

        public ExposureEvent(long timestamp, string flagKey, string allocationKey, string variationKey, string subjectId, IReadOnlyDictionary<string, object> subjectAttributes)
        {
            Timestamp = timestamp;
            FlagKey = flagKey;
            AllocationKey = allocationKey;
            VariationKey = variationKey;
            SubjectId = subjectId;
            SubjectAttributes = subjectAttributes ?? new Dictionary<string, object>();
        }

        public string ToJson()
        {
            var dto = new ExposureEventDto
            {
                Timestamp = Timestamp,
                Flag = new KeyDto { Key = FlagKey },
                Allocation = new KeyDto { Key = AllocationKey },
                Variant = new KeyDto { Key = VariationKey },
                Subject = new SubjectDto
                {
                    Id = SubjectId,
                    Attributes = SubjectAttributes.Count > 0 ? SubjectAttributes : null,
                },
            };
            return JsonConvert.SerializeObject(dto);
        }

        private class ExposureEventDto
        {
            [JsonProperty("timestamp")]
            public long Timestamp { get; set; }

            [JsonProperty("flag")]
            public KeyDto Flag { get; set; }

            [JsonProperty("allocation")]
            public KeyDto Allocation { get; set; }

            [JsonProperty("variant")]
            public KeyDto Variant { get; set; }

            [JsonProperty("subject")]
            public SubjectDto Subject { get; set; }
        }

        private class KeyDto
        {
            [JsonProperty("key")]
            public string Key { get; set; }
        }

        private class SubjectDto
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("attributes", NullValueHandling = NullValueHandling.Ignore)]
            public IReadOnlyDictionary<string, object> Attributes { get; set; }
        }
    }
}
