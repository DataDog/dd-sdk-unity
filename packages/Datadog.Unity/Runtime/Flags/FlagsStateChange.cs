// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Describes a FlagsClient state transition delivered via <see cref="FlagsClient.StateChanged"/>.
    /// When <see cref="Old"/> equals <see cref="New"/> the event is a replay of the current state
    /// emitted immediately to a new subscriber rather than a real transition.
    /// </summary>
    public class FlagsStateChange
    {
        public readonly FlagsClientState Old;
        public readonly FlagsClientState New;

        public FlagsStateChange(FlagsClientState oldState, FlagsClientState newState)
        {
            Old = oldState;
            New = newState;
        }
    }
}
