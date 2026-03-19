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
        public string Id { get; set; }

        [JsonProperty("attributes", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyDictionary<string, string> Attributes { get; set; }
    }

    /// <summary>
    /// Exposure event sent to /api/v2/exposures (NDJSON, one object per line).
    /// Serialise directly with <c>JsonConvert.SerializeObject</c>.
    /// </summary>
    internal class ExposureEvent
    {
        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        [JsonProperty("flag")]
        public FlagRef Flag { get; set; }

        [JsonProperty("allocation")]
        public FlagRef Allocation { get; set; }

        [JsonProperty("variant")]
        public FlagRef Variant { get; set; }

        [JsonProperty("subject")]
        public ExposureSubject Subject { get; set; }
    }
}
