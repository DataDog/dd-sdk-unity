// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Interface for evaluating feature flags. Use <c>DdFlags.Instance.CreateClient()</c> to obtain an instance.
    /// </summary>
    public interface IFlagsClient : IDisposable
    {
        /// <summary>Gets the current state of the client.</summary>
        FlagsClientState State { get; }

        /// <summary>
        /// Subscribes to client state changes. The handler is invoked immediately with the
        /// current state (where <c>Old == New</c>) and on every subsequent transition.
        /// </summary>
        event EventHandler<FlagsStateChange> StateChanged;

        /// <summary>Sets the evaluation context and fetches precomputed flag assignments.</summary>
        void SetEvaluationContext(FlagsEvaluationContext context, Action<bool> onComplete = null);

        bool GetBooleanValue(string key, bool defaultValue);
        string GetStringValue(string key, string defaultValue);
        int GetIntegerValue(string key, int defaultValue);
        double GetDoubleValue(string key, double defaultValue);
        object GetObjectValue(string key, object defaultValue);

        FlagDetails<bool> GetBooleanDetails(string key, bool defaultValue);
        FlagDetails<string> GetStringDetails(string key, string defaultValue);
        FlagDetails<int> GetIntegerDetails(string key, int defaultValue);
        FlagDetails<double> GetDoubleDetails(string key, double defaultValue);
        FlagDetails<T> GetDetails<T>(string key, T defaultValue);

        /// <summary>Flushes any pending aggregated evaluation events.</summary>
        void Flush();
    }
}
