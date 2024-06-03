// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;

namespace Datadog.Unity
{
    /// <summary>
    /// An interface to wrap calls to various Datadog platforms.
    /// </summary>
    internal interface IDatadogPlatform
    {
        void Init(DatadogConfigurationOptions options);

        void SetVerbosity(CoreLoggerLevel logLevel);

        void SetTrackingConsent(TrackingConsent trackingConsent);

        DdLogger CreateLogger(DatadogLoggingOptions options, DatadogWorker worker);

        void SetUserInfo(string id, string name, string email, Dictionary<string, object> extraInfo);

        void AddUserExtraInfo(Dictionary<string, object> extraInfo);

        IDdRum InitRum(DatadogConfigurationOptions options);

        void SendDebugTelemetry(string message);

        void SendErrorTelemetry(string message, string stack, string kind);

        void ClearAllData();
    }

    /// <summary>
    /// The logging level for the DatadogSdk Core.
    /// </summary>
    public enum CoreLoggerLevel
    {
        Debug = 0,
        Warn = 1,
        Error = 2,
        Critical = 3,
    }
}
