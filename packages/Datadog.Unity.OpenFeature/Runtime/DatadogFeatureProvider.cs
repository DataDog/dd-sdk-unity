// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Datadog.Unity.Flags;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace Datadog.Unity.Flags.OpenFeature
{
    /// <summary>
    /// OpenFeature provider backed by a Datadog <see cref="IFlagsClient"/>.
    /// Flag evaluation is a synchronous in-memory lookup with no network calls during execution.
    ///
    /// Obtain a client via <see cref="DdFlags.CreateClient"/> and register
    /// the provider with OpenFeature:
    ///
    /// <code>
    /// DdFlags.Enable(new FlagsConfiguration());
    /// var client = DdFlags.Instance.CreateClient();
    /// await OpenFeature.Api.Instance.SetProviderAsync(new DatadogFeatureProvider(client));
    /// client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), onComplete: _ => { });
    ///
    /// var ofClient = OpenFeature.Api.Instance.GetClient();
    /// var enabled = await ofClient.GetBooleanValueAsync("show-feature", false);
    /// </code>
    /// </summary>
    public class DatadogFeatureProvider : FeatureProvider
    {
        internal const string ProviderName = "Datadog";

        private readonly IFlagsClient _client;
        private readonly SemaphoreSlim _contextLock = new SemaphoreSlim(1, 1);
        private string _lastContextFingerprint;

        public DatadogFeatureProvider(IFlagsClient client)
        {
            _client = client;
        }

        public override Metadata GetMetadata()
        {
            return new Metadata(ProviderName);
        }

        public override async Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
            string flagKey,
            bool defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureContextAsync(context, cancellationToken).ConfigureAwait(false);
            return Resolve(flagKey, defaultValue);
        }

        public override async Task<ResolutionDetails<string>> ResolveStringValueAsync(
            string flagKey,
            string defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureContextAsync(context, cancellationToken).ConfigureAwait(false);
            return Resolve(flagKey, defaultValue);
        }

        public override async Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
            string flagKey,
            int defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureContextAsync(context, cancellationToken).ConfigureAwait(false);
            return Resolve(flagKey, defaultValue);
        }

        public override async Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
            string flagKey,
            double defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureContextAsync(context, cancellationToken).ConfigureAwait(false);
            return Resolve(flagKey, defaultValue);
        }

        public override async Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
            string flagKey,
            Value defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            await EnsureContextAsync(context, cancellationToken).ConfigureAwait(false);

            var flagDetails = _client.GetDetails<object>(flagKey, null);

            if (flagDetails.Error.HasValue)
            {
                return new ResolutionDetails<Value>(
                    flagKey, defaultValue, MapErrorType(flagDetails.Error.Value),
                    reason: flagDetails.Reason, variant: flagDetails.Variant);
            }

            var value = flagDetails.AsOpenFeatureValue() ?? defaultValue;
            return new ResolutionDetails<Value>(
                flagKey, value, variant: flagDetails.Variant, reason: flagDetails.Reason);
        }

        private ResolutionDetails<T> Resolve<T>(string flagKey, T defaultValue)
        {
            var flagDetails = _client.GetDetails(flagKey, defaultValue);

            if (flagDetails.Error.HasValue)
            {
                return new ResolutionDetails<T>(
                    flagKey, defaultValue, MapErrorType(flagDetails.Error.Value),
                    reason: flagDetails.Reason, variant: flagDetails.Variant);
            }

            return new ResolutionDetails<T>(
                flagKey, flagDetails.Value, variant: flagDetails.Variant,
                reason: flagDetails.Reason);
        }

        /// <summary>
        /// If <paramref name="context"/> is non-null and its fingerprint differs from the last
        /// fetched context, triggers a <see cref="IFlagsClient.SetEvaluationContext"/> fetch and
        /// awaits completion before returning.
        /// </summary>
        private async Task EnsureContextAsync(EvaluationContext context, CancellationToken cancellationToken)
        {
            if (context == null || string.IsNullOrEmpty(context.TargetingKey))
            {
                return;
            }

            var fingerprint = ComputeFingerprint(context);
            if (fingerprint == _lastContextFingerprint)
            {
                return;
            }

            await _contextLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Double-check after acquiring the lock
                if (fingerprint == _lastContextFingerprint)
                {
                    return;
                }

                var tcs = new TaskCompletionSource<bool>();
                _client.SetEvaluationContext(ToFlagsContext(context), success =>
                {
                    tcs.TrySetResult(success);
                });
                await tcs.Task.ConfigureAwait(false);

                _lastContextFingerprint = fingerprint;
            }
            finally
            {
                _contextLock.Release();
            }
        }

        private static string ComputeFingerprint(EvaluationContext context)
        {
            // Use manual iteration via GetEnumerator() to avoid a compile-time dependency on
            // System.Collections.Immutable (returned by EvaluationContext.AsDictionary()).
            var sorted = new SortedDictionary<string, string>();
            using var enumerator = context.GetEnumerator();
            while (enumerator.MoveNext())
            {
                sorted[enumerator.Current.Key] = enumerator.Current.Value?.ToString() ?? string.Empty;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append(context.TargetingKey).Append(':');
            foreach (var kvp in sorted)
            {
                sb.Append(kvp.Key).Append('=').Append(kvp.Value).Append(';');
            }
            return sb.ToString();
        }

        private static FlagsEvaluationContext ToFlagsContext(EvaluationContext context)
        {
            var attrs = new Dictionary<string, object>();
            using var enumerator = context.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var v = enumerator.Current.Value;
                attrs[enumerator.Current.Key] = v?.AsString ?? v?.ToString();
            }

            if (attrs.Count == 0)
            {
                return new FlagsEvaluationContext(context.TargetingKey);
            }

            return new FlagsEvaluationContext(context.TargetingKey, attrs);
        }

        private static ErrorType MapErrorType(FlagEvaluationError error)
        {
            switch (error)
            {
                case FlagEvaluationError.FlagNotFound:
                    return ErrorType.FlagNotFound;
                case FlagEvaluationError.TypeMismatch:
                    return ErrorType.TypeMismatch;
                case FlagEvaluationError.ProviderNotReady:
                    return ErrorType.ProviderNotReady;
                default:
                    return ErrorType.General;
            }
        }
    }
}
