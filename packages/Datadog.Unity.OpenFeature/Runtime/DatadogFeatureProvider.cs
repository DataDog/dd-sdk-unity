// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

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

        public DatadogFeatureProvider(IFlagsClient client)
        {
            _client = client;
        }

        public override Metadata GetMetadata()
        {
            return new Metadata(ProviderName);
        }

        public override Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(
            string flagKey,
            bool defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Resolve(flagKey, defaultValue));
        }

        public override Task<ResolutionDetails<string>> ResolveStringValueAsync(
            string flagKey,
            string defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Resolve(flagKey, defaultValue));
        }

        public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
            string flagKey,
            int defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Resolve(flagKey, defaultValue));
        }

        public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
            string flagKey,
            double defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Resolve(flagKey, defaultValue));
        }

        public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
            string flagKey,
            Value defaultValue,
            EvaluationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var flagDetails = _client.GetDetails<object>(flagKey, null);

            if (flagDetails.Error.HasValue)
            {
                return Task.FromResult(new ResolutionDetails<Value>(
                    flagKey, defaultValue, MapErrorType(flagDetails.Error.Value),
                    reason: flagDetails.Reason, variant: flagDetails.Variant));
            }

            var value = flagDetails.AsOpenFeatureValue() ?? defaultValue;
            return Task.FromResult(new ResolutionDetails<Value>(
                flagKey, value, variant: flagDetails.Variant, reason: flagDetails.Reason));
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

        private static ErrorType MapErrorType(FlagEvaluationError error)
        {
            switch (error)
            {
                case FlagEvaluationError.FlagNotFound:
                    return ErrorType.FlagNotFound;
                case FlagEvaluationError.TypeMismatch:
                    return ErrorType.TypeMismatch;
                default:
                    return ErrorType.General;
            }
        }
    }
}
