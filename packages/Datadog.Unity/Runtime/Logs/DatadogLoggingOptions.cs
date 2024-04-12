// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Diagnostics.CodeAnalysis;
using UnityEngine.Scripting;

namespace Datadog.Unity.Logs
{
    [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "Public fields needed for JSON serialization")]

    public class DatadogLoggingOptions
    {
        [Preserve]
        public string Service = null;

        [Preserve]
        public string Name = null;

        [Preserve]
        public bool NetworkInfoEnabled = false;

        [Preserve]
        public bool BundleWithRumEnabled = true;

        [Preserve]
        public float RemoteSampleRate = 100.0f;

        [Preserve]
        public DdLogLevel RemoteLogThreshold = DdLogLevel.Debug;
    }
}
