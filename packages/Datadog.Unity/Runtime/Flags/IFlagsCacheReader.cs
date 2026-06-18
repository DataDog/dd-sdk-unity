// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.Flags
{
    internal interface IFlagsCacheReader
    {
        /// <summary>
        /// Reads the cached flag envelope from the cache store.
        /// Returns <c>null</c> when no cached data exists or the stored data is corrupt.
        /// </summary>
        /// <param name="context">
        /// The evaluation context at read time. Reserved for future use
        /// (e.g., per-context cache scoping). Not used by the current implementation.
        /// </param>
        FlagsCacheEnvelopeDto? Read(FlagsEvaluationContext context);
    }
}
