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
        /// The evaluation context to serialize alongside the flag payload. Used to restore
        /// context after bootstrap so exposure tracking works without requiring a
        /// SetEvaluationContext call. May be null if no context has been established yet.
        /// </param>
        void Write(string rawJson, FlagsEvaluationContext context);
    }
}
