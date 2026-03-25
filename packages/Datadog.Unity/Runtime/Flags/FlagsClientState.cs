// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Represents the current state of a FlagsClient.
    /// </summary>
    public enum FlagsClientState
    {
        /// <summary>The client is not ready to evaluate flags.</summary>
        NotReady,

        /// <summary>The client has loaded flags and is ready for evaluation.</summary>
        Ready,

        /// <summary>The client is fetching new flags for a context change.</summary>
        Reconciling,

        /// <summary>The client has stale cached flags (network failed).</summary>
        Stale,

        /// <summary>An unrecoverable error occurred.</summary>
        Error,
    }
}
