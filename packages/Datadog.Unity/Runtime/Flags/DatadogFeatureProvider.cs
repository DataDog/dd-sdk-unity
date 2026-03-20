// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// OpenFeature provider backed by a Datadog <see cref="FlagsClient"/>.
    /// Flag evaluation is a synchronous in-memory lookup — no network calls on the hot path.
    ///
    /// Obtain an instance from <see cref="DdFlags.CreateProvider"/> and register it with OpenFeature:
    ///
    /// <code>
    /// DdFlags.Enable(new FlagsConfiguration());
    /// DdFlags.CreateClient();
    /// await OpenFeature.Api.Instance.SetProviderAsync(DdFlags.CreateProvider());
    /// DdFlags.SetEvaluationContext(new FlagsEvaluationContext("user-123"));
    ///
    /// var ofClient = OpenFeature.Api.Instance.GetClient();
    /// var enabled = await ofClient.GetBooleanValueAsync("show-feature", false);
    /// </code>
    /// </summary>
    public class DatadogFeatureProvider : FeatureProvider
    {
        internal const string ProviderName = "Datadog";

        private readonly FlagsClient _client;

        internal DatadogFeatureProvider(FlagsClient client)
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

            var value = ToOpenFeatureValue(flagDetails.Value) ?? defaultValue;
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
            if (obj is long l) return new Value((double)l);
            if (obj is double d) return new Value(d);
            if (obj is float f) return new Value((double)f);
            if (obj is string s) return new Value(s);
            if (obj is Dictionary<string, object> dict)
            {
                var converted = new Dictionary<string, Value>();
                foreach (var kvp in dict)
                {
                    var val = ToOpenFeatureValue(kvp.Value);
                    if (val != null)
                    {
                        converted[kvp.Key] = val;
                    }
                }
                return new Value(new Structure(converted));
            }
            if (obj is IList<object> list)
            {
                var values = new List<Value>(list.Count);
                foreach (var item in list)
                {
                    var val = ToOpenFeatureValue(item);
                    if (val != null)
                    {
                        values.Add(val);
                    }
                }
                return new Value(values);
            }
            return new Value(obj.ToString());
        }
    }
}

