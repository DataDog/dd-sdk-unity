// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Aggregates flag evaluation events and flushes them periodically or when the max count is reached.
    /// Matching the iOS EvaluationAggregator pattern: dimensions = (flagKey, variantKey, allocationKey,
    /// targetingKey, errorMessage, contextHash). Flush triggers: timer (10s default), max aggregations (1000).
    /// </summary>
    internal class EvaluationAggregator : IDisposable
    {
        internal struct AggregationKey : IEquatable<AggregationKey>
        {
            public readonly string FlagKey;
            public readonly string VariantKey;
            public readonly string AllocationKey;
            public readonly string TargetingKey;
            public readonly string ErrorMessage;
            public readonly int ContextHash;

            public AggregationKey(
                string flagKey,
                string variantKey,
                string allocationKey,
                string targetingKey,
                string errorMessage,
                Dictionary<string, object> context)
            {
                FlagKey = flagKey;
                VariantKey = variantKey;
                AllocationKey = allocationKey;
                TargetingKey = targetingKey;
                ErrorMessage = errorMessage;

                // Deterministic hash of context attributes (sorted keys)
                unchecked
                {
                    var hash = 17;
                    if (context != null)
                    {
                        foreach (var key in context.Keys.OrderBy(k => k, StringComparer.Ordinal))
                        {
                            hash = hash * 31 + key.GetHashCode();
                            hash = hash * 31 + (context[key]?.GetHashCode() ?? 0);
                        }
                    }
                    ContextHash = hash;
                }
            }

            public bool Equals(AggregationKey other)
            {
                return FlagKey == other.FlagKey
                    && VariantKey == other.VariantKey
                    && AllocationKey == other.AllocationKey
                    && TargetingKey == other.TargetingKey
                    && ErrorMessage == other.ErrorMessage
                    && ContextHash == other.ContextHash;
            }

            public override bool Equals(object obj)
            {
                return obj is AggregationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 31 + (FlagKey?.GetHashCode() ?? 0);
                    hash = hash * 31 + (VariantKey?.GetHashCode() ?? 0);
                    hash = hash * 31 + (AllocationKey?.GetHashCode() ?? 0);
                    hash = hash * 31 + (TargetingKey?.GetHashCode() ?? 0);
                    hash = hash * 31 + (ErrorMessage?.GetHashCode() ?? 0);
                    hash = hash * 31 + ContextHash;
                    return hash;
                }
            }
        }

        internal class AggregatedEvaluation
        {
            public string FlagKey;
            public string VariantKey;
            public string AllocationKey;
            public string TargetingKey;
            public string TargetingRuleKey;
            public string ErrorMessage;
            public Dictionary<string, object> Context;
            public long FirstEvaluation;
            public long LastEvaluation;
            public int EvaluationCount;
            public bool? RuntimeDefaultUsed;

            public FlagEvaluationEvent ToFlagEvaluationEvent()
            {
                return new FlagEvaluationEvent
                {
                    Timestamp = FirstEvaluation,
                    FlagKey = FlagKey,
                    FirstEvaluation = FirstEvaluation,
                    LastEvaluation = LastEvaluation,
                    EvaluationCount = EvaluationCount,
                    VariantKey = RuntimeDefaultUsed == true ? null : VariantKey,
                    AllocationKey = RuntimeDefaultUsed == true ? null : AllocationKey,
                    TargetingRuleKey = TargetingRuleKey,
                    TargetingKey = TargetingKey,
                    RuntimeDefaultUsed = RuntimeDefaultUsed,
                    ErrorMessage = ErrorMessage,
                    EvaluationAttributes = Context?.Count > 0 ? Context : null,
                };
            }
        }

        public const int DefaultMaxAggregations = 1_000;
        public const float DefaultFlushIntervalSeconds = 10.0f;
        public const float MinFlushIntervalSeconds = 1.0f;
        public const float MaxFlushIntervalSeconds = 60.0f;

        private readonly object _lock = new();
        private readonly int _maxAggregations;
        private readonly float _flushIntervalSeconds;
        private Dictionary<AggregationKey, AggregatedEvaluation> _aggregations = new();
        private Timer _flushTimer;
        private Action<List<FlagEvaluationEvent>> _onFlush;
        private bool _disposed;

        public EvaluationAggregator(
            Action<List<FlagEvaluationEvent>> onFlush,
            float flushIntervalSeconds = DefaultFlushIntervalSeconds,
            int maxAggregations = DefaultMaxAggregations)
        {
            _onFlush = onFlush;
            _flushIntervalSeconds = Math.Max(MinFlushIntervalSeconds, Math.Min(MaxFlushIntervalSeconds, flushIntervalSeconds));
            _maxAggregations = maxAggregations;

            var intervalMs = (int)(_flushIntervalSeconds * 1000);
            _flushTimer = new Timer(OnTimerElapsed, null, intervalMs, intervalMs);
        }

        public void RecordEvaluation(
            string flagKey,
            FlagAssignment assignment,
            FlagsEvaluationContext evaluationContext,
            string flagError)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var key = new AggregationKey(
                flagKey: flagKey,
                variantKey: assignment?.VariationKey,
                allocationKey: assignment?.AllocationKey,
                targetingKey: evaluationContext?.TargetingKey,
                errorMessage: flagError,
                context: evaluationContext?.Attributes);

            lock (_lock)
            {
                if (_aggregations.TryGetValue(key, out var existing))
                {
                    existing.EvaluationCount += 1;
                    existing.LastEvaluation = now;
                }
                else
                {
                    var reason = assignment?.Reason;
                    var runtimeDefaultUsed = reason == "DEFAULT" || flagError != null;

                    _aggregations[key] = new AggregatedEvaluation
                    {
                        FlagKey = flagKey,
                        VariantKey = assignment?.VariationKey,
                        AllocationKey = assignment?.AllocationKey,
                        TargetingKey = evaluationContext?.TargetingKey,
                        TargetingRuleKey = null,
                        ErrorMessage = flagError,
                        Context = evaluationContext?.Attributes,
                        FirstEvaluation = now,
                        LastEvaluation = now,
                        EvaluationCount = 1,
                        RuntimeDefaultUsed = runtimeDefaultUsed ? true : (bool?)null,
                    };
                }

                if (_aggregations.Count >= _maxAggregations)
                {
                    FlushInternal();
                }
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                FlushInternal();
            }
        }

        private void FlushInternal()
        {
            if (_aggregations.Count == 0)
            {
                return;
            }

            var events = _aggregations.Values
                .Select(a => a.ToFlagEvaluationEvent())
                .ToList();

            _aggregations.Clear();

            _onFlush?.Invoke(events);
        }

        private void OnTimerElapsed(object state)
        {
            Flush();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _flushTimer?.Dispose();
            _flushTimer = null;

            // Final flush
            Flush();
        }
    }
}
