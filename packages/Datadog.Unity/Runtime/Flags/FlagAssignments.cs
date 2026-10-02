// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Datadog.Unity.Flags
{
    /// <summary>Keeps the assignment map and its lookup encoding together.</summary>
    internal sealed class FlagAssignments
    {
        internal readonly IReadOnlyDictionary<string, FlagAssignment> Flags;
        internal readonly FlagKeyObfuscation Obfuscation;

        internal FlagAssignments(Dictionary<string, FlagAssignment> flags, FlagKeyObfuscation obfuscation = null)
        {
            Flags = new ReadOnlyDictionary<string, FlagAssignment>(new Dictionary<string, FlagAssignment>(flags));
            Obfuscation = obfuscation;
        }
    }
}
