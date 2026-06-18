// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using Newtonsoft.Json;

namespace Datadog.Unity.Flags
{
    internal class FlagsCacheEnvelopeDto
    {
        [JsonProperty("cachedAt")]
        public string CachedAt { get; set; }

        [JsonProperty("payload")]
        public string Payload { get; set; }
    }
}
