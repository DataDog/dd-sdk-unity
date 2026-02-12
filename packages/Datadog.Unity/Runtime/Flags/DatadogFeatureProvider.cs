// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// OpenFeature provider backed by Datadog's precompute-based feature flag evaluation.
    /// Flag evaluation is a synchronous in-memory lookup — no network calls or JNI bridging
    /// on the hot path.
    ///
    /// Usage:
    /// <code>
    /// DdFlags.Enable(new FlagsConfiguration());
    /// var client = DdFlags.CreateClient();
    /// client.SetEvaluationContext(new FlagsEvaluationContext("user-123"));
    ///
    /// // Use via OpenFeature API
    /// var ofClient = Api.Instance.GetClient();
    /// var showFeature = await ofClient.GetBooleanValueAsync("show-feature", false);
    /// </code>
    /// </summary>
    internal class DatadogFeatureProvider : FeatureProvider
    {
        internal const string ProviderName = "Datadog";

        private FlagsClient _client;

        internal DatadogFeatureProvider()
        {
        }

        internal void SetClient(FlagsClient client)
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
            EvaluationContext context = null,
            CancellationToken cancellationToken = default)
        {
            var details = Resolve(flagKey, defaultValue);
            return Task.FromResult(details);
        }

        public override Task<ResolutionDetails<string>> ResolveStringValueAsync(
            string flagKey,
            string defaultValue,
            EvaluationContext context = null,
            CancellationToken cancellationToken = default)
        {
            var details = Resolve(flagKey, defaultValue);
            return Task.FromResult(details);
        }

        public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(
            string flagKey,
            int defaultValue,
            EvaluationContext context = null,
            CancellationToken cancellationToken = default)
        {
            var details = Resolve(flagKey, defaultValue);
            return Task.FromResult(details);
        }

        public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(
            string flagKey,
            double defaultValue,
            EvaluationContext context = null,
            CancellationToken cancellationToken = default)
        {
            var details = Resolve(flagKey, defaultValue);
            return Task.FromResult(details);
        }

        public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(
            string flagKey,
            Value defaultValue,
            EvaluationContext context = null,
            CancellationToken cancellationToken = default)
        {
            if (_client == null)
            {
                return Task.FromResult(new ResolutionDetails<Value>(
                    flagKey, defaultValue, ErrorType.ProviderNotReady, errorMessage: "Provider not initialized"));
            }

            var flagDetails = _client.GetDetails<object>(flagKey, null);

            if (flagDetails.Error.HasValue)
            {
                return Task.FromResult(new ResolutionDetails<Value>(
                    flagKey, defaultValue, MapErrorType(flagDetails.Error.Value),
                    reason: flagDetails.Reason, variant: flagDetails.Variant));
            }

            var value = ToOpenFeatureValue(flagDetails.Value) ?? defaultValue;
            return Task.FromResult(new ResolutionDetails<Value>(
                flagKey, value, variant: flagDetails.Variant, reason: flagDetails.Reason));
        }

        private ResolutionDetails<T> Resolve<T>(string flagKey, T defaultValue)
        {
            if (_client == null)
            {
                return new ResolutionDetails<T>(
                    flagKey, defaultValue, ErrorType.ProviderNotReady,
                    errorMessage: "Provider not initialized");
            }

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
                case FlagEvaluationError.ProviderNotReady:
                    return ErrorType.ProviderNotReady;
                case FlagEvaluationError.FlagNotFound:
                    return ErrorType.FlagNotFound;
                case FlagEvaluationError.TypeMismatch:
                    return ErrorType.TypeMismatch;
                default:
                    return ErrorType.General;
            }
        }

        private static Value ToOpenFeatureValue(object obj)
        {
            if (obj == null) return null;
            if (obj is bool b) return new Value(b);
            if (obj is int i) return new Value(i);
            if (obj is long l) return new Value((int)l);
            if (obj is double d) return new Value(d);
            if (obj is float f) return new Value((double)f);
            if (obj is string s) return new Value(s);
            if (obj is Dictionary<string, object> dict)
            {
                var structure = new Structure(new Dictionary<string, Value>());
                foreach (var kvp in dict)
                {
                    var val = ToOpenFeatureValue(kvp.Value);
                    if (val != null)
                    {
                        structure.Add(kvp.Key, val);
                    }
                }
                return new Value(structure);
            }
            return new Value(obj.ToString());
        }
    }
}
