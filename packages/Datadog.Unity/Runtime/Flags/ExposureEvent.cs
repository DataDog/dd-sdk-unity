// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Datadog.Unity.Flags
{
    internal class ExposureSubject
    {
        [JsonProperty("id")]
        public readonly string Id;

        [JsonProperty("attributes", NullValueHandling = NullValueHandling.Ignore)]
        public readonly IReadOnlyDictionary<string, string> Attributes;

        public ExposureSubject(string id, IReadOnlyDictionary<string, string> attributes = null)
        {
            Id = id;
            Attributes = attributes;
        }
    }

    /// <summary>
    /// Exposure event sent to /api/v2/exposures (NDJSON, one object per line).
    /// Serialise directly with <c>JsonConvert.SerializeObject</c>.
    /// </summary>
    internal class ExposureEvent
    {
        [JsonProperty("timestamp")]
        public readonly long Timestamp;

        [JsonProperty("flag")]
        public readonly FlagRef Flag;

        [JsonProperty("allocation")]
        public readonly FlagRef Allocation;

        [JsonProperty("variant")]
        public readonly FlagRef Variant;

        [JsonProperty("subject")]
        public readonly ExposureSubject Subject;

        public ExposureEvent(
            long timestamp,
            FlagRef flag,
            FlagRef allocation,
            FlagRef variant,
            ExposureSubject subject)
        {
            Timestamp = timestamp;
            Flag = flag;
            Allocation = allocation;
            Variant = variant;
            Subject = subject;
        }
    }
}
