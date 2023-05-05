// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Diagnostics.CodeAnalysis;

namespace Datadog.Unity.Logs
{
    [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "Public fields needed for JSON serialization")]

    public class DatadogLoggingOptions
    {
        public string ServiceName = null;

        public string LoggerName = null;

        public bool SendNetworkInfo = false;

        public bool SendToDatadog = true;

        public DdLogLevel DatadogReportingThreshold = DdLogLevel.Debug;
    }
}
