// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.Flags
{
    internal interface IFlagsCacheWriter
    {
        /// <summary>
        /// Writes the raw server response JSON to the cache store.
        /// </summary>
        /// <param name="rawJson">The raw JSON string from the server response.</param>
        /// <param name="context">
        /// The evaluation context in effect at write time. Reserved for future use
        /// (e.g., per-context cache scoping). Not used by the current implementation.
        /// </param>
        void Write(string rawJson, FlagsEvaluationContext context);
    }
}
