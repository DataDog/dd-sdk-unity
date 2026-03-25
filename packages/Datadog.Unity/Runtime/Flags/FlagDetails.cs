// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Error codes for flag evaluation failures.
    /// </summary>
    public enum FlagEvaluationError
    {
        /// <summary>The provider is not ready to evaluate flags.</summary>
        ProviderNotReady,

        /// <summary>The flag key was not found.</summary>
        FlagNotFound,

        /// <summary>The flag type does not match the requested type.</summary>
        TypeMismatch,
    }

    /// <summary>
    /// Detailed result of a flag evaluation including value, variant, reason, and error information.
    /// </summary>
    /// <typeparam name="T">The type of the flag value.</typeparam>
    public class FlagDetails<T>
    {
        internal FlagDetails(string key, T value, string variant = null, string reason = null, FlagEvaluationError? error = null)
        {
            Key = key;
            Value = value;
            Variant = variant;
            Reason = reason;
            Error = error;
        }

        /// <summary>Gets the flag key.</summary>
        public string Key { get; }

        /// <summary>Gets the evaluated or default value.</summary>
        public T Value { get; }

        /// <summary>Gets the variant served (null if not found).</summary>
        public string Variant { get; }

        /// <summary>Gets the evaluation reason (null if not found).</summary>
        public string Reason { get; }

        /// <summary>Gets the evaluation error (null if successful).</summary>
        public FlagEvaluationError? Error { get; }
    }
}
