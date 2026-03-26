// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// A no-op implementation of <see cref="IFlagsClient"/> that returns the caller-supplied
    /// default value for every evaluation. Used when the Flags SDK is not operational —
    /// either because <see cref="DdFlags.Enable"/> has not been called, or because the
    /// required <c>Env</c> setting was not configured in a production build.
    /// </summary>
    internal class NoopFlagsClient : IFlagsClient
    {
        private readonly string _reason;
        private readonly IInternalLogger _logger;

        internal NoopFlagsClient(string reason, IInternalLogger logger = null)
        {
            _reason = reason;
            _logger = logger;
        }

        public FlagsClientState State => FlagsClientState.NotReady;

        public event EventHandler<FlagsStateChange> StateChanged
        {
            add
            {
                // Replay current (NotReady) state immediately, matching IFlagsClient contract.
                // No further transitions will ever fire on a noop client.
                if (value == null) return;
                value(this, new FlagsStateChange(FlagsClientState.NotReady, FlagsClientState.NotReady));
            }
            remove { }
        }

        public void SetEvaluationContext(FlagsEvaluationContext context, Action<bool> onComplete = null)
        {
            onComplete?.Invoke(false);
        }

        public bool GetBooleanValue(string key, bool defaultValue) => GetDetails(key, defaultValue).Value;
        public string GetStringValue(string key, string defaultValue) => GetDetails(key, defaultValue).Value;
        public int GetIntegerValue(string key, int defaultValue) => GetDetails(key, defaultValue).Value;
        public double GetDoubleValue(string key, double defaultValue) => GetDetails(key, defaultValue).Value;
        public object GetObjectValue(string key, object defaultValue) => GetDetails(key, defaultValue).Value;

        public FlagDetails<bool> GetBooleanDetails(string key, bool defaultValue) => GetDetails(key, defaultValue);
        public FlagDetails<string> GetStringDetails(string key, string defaultValue) => GetDetails(key, defaultValue);
        public FlagDetails<int> GetIntegerDetails(string key, int defaultValue) => GetDetails(key, defaultValue);
        public FlagDetails<double> GetDoubleDetails(string key, double defaultValue) => GetDetails(key, defaultValue);

        public FlagDetails<T> GetDetails<T>(string key, T defaultValue)
        {
            _logger?.Log(DdLogLevel.Debug,
                $"[DdFlags] '{key}' — returning developer default ({_reason}: client not configured)");
            return new FlagDetails<T>(key, defaultValue, reason: _reason);
        }

        public void Flush() { }

        public void Dispose() { }
    }
}
