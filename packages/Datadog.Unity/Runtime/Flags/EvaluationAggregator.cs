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
        private const string ReasonDefault = "DEFAULT";
        private const string ReasonError = "ERROR";

        internal struct AggregationKey : IEquatable<AggregationKey>
        {
            public readonly string FlagKey;
            public readonly string VariantKey;
            public readonly string AllocationKey;
            public readonly string TargetingKey;
            public readonly string ErrorMessage;
            public readonly int ContextHash;
            public readonly IReadOnlyDictionary<string, object> Context;

            public AggregationKey(
                string flagKey,
                string variantKey,
                string allocationKey,
                string targetingKey,
                string errorMessage,
                IReadOnlyDictionary<string, object> context)
            {
                FlagKey = flagKey;
                VariantKey = variantKey;
                AllocationKey = allocationKey;
                TargetingKey = targetingKey;
                ErrorMessage = errorMessage;
                Context = context;

                // Deterministic hash of context attributes (sorted keys).
                // unchecked: integer overflow is intentional; hash wrapping is acceptable.
                unchecked
                {
                    // 17 and 31 are standard primes for polynomial hash combining (Bloch, Effective Java).
                    var hash = 17;
                    if (context != null)
                    {
                        foreach (var key in context.Keys.OrderBy(k => k, StringComparer.Ordinal))
                        {
                            hash = hash * 31 + key.GetHashCode();
                            // Note: context values are primitives in practice (string, bool, int, double),
                            // so GetHashCode() here is stable even though object allows mutation.
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
                    && DictionaryEquals(Context, other.Context);
            }

            public override bool Equals(object obj)
            {
                return obj is AggregationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                // unchecked: integer overflow is intentional; hash wrapping is acceptable.
                // 17 and 31 are standard primes for polynomial hash combining (Bloch, Effective Java).
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

            private static bool DictionaryEquals(IReadOnlyDictionary<string, object> a, IReadOnlyDictionary<string, object> b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null) return false;
                if (a.Count != b.Count) return false;
                foreach (var kvp in a)
                {
                    if (!b.TryGetValue(kvp.Key, out var bVal)) return false;
                    if (!Equals(kvp.Value, bVal)) return false;
                }
                return true;
            }
        }

        internal class AggregatedEvaluation
        {
            public readonly string FlagKey;
            public readonly string VariantKey;
            public readonly string AllocationKey;
            public readonly string TargetingKey;
            public readonly string TargetingRuleKey;
            public readonly string ErrorMessage;
            public readonly IReadOnlyDictionary<string, object> Context;
            public readonly long FirstEvaluation;
            public readonly bool? RuntimeDefaultUsed;

            // Mutable: updated on each subsequent evaluation for the same dimensions.
            public long LastEvaluation;
            public int EvaluationCount;

            public AggregatedEvaluation(
                string flagKey,
                string variantKey,
                string allocationKey,
                string targetingKey,
                string targetingRuleKey,
                string errorMessage,
                IReadOnlyDictionary<string, object> context,
                long firstEvaluation,
                bool? runtimeDefaultUsed)
            {
                FlagKey = flagKey;
                VariantKey = variantKey;
                AllocationKey = allocationKey;
                TargetingKey = targetingKey;
                TargetingRuleKey = targetingRuleKey;
                ErrorMessage = errorMessage;
                Context = context;
                FirstEvaluation = firstEvaluation;
                LastEvaluation = firstEvaluation;
                EvaluationCount = 1;
                RuntimeDefaultUsed = runtimeDefaultUsed;
            }

            public FlagEvaluationEvent ToFlagEvaluationEvent()
            {
                return new FlagEvaluationEvent(
                    timestamp: FirstEvaluation,
                    flagKey: FlagKey,
                    firstEvaluation: FirstEvaluation,
                    lastEvaluation: LastEvaluation,
                    evaluationCount: EvaluationCount,
                    variantKey: RuntimeDefaultUsed == true ? null : VariantKey,
                    allocationKey: RuntimeDefaultUsed == true ? null : AllocationKey,
                    targetingRuleKey: TargetingRuleKey,
                    targetingKey: TargetingKey,
                    runtimeDefaultUsed: RuntimeDefaultUsed,
                    errorMessage: ErrorMessage,
                    evaluationAttributes: Context?.Count > 0 ? Context : null);
            }
        }

        public const int DefaultMaxAggregations = 1_000;
        public const float DefaultFlushIntervalSeconds = 10.0f;
        public const float MinFlushIntervalSeconds = 1.0f;
        public const float MaxFlushIntervalSeconds = 60.0f;

        private readonly object _lock = new();
        private readonly int _maxAggregations;
        private readonly float _flushIntervalSeconds;
        private readonly SynchronizationContext _mainThreadContext;
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

            // Capture Unity's main-thread SynchronizationContext so the timer
            // callback can dispatch back to the main thread (UnityWebRequest
            // and SystemInfo APIs are main-thread-only).
            _mainThreadContext = SynchronizationContext.Current;

            var intervalMs = (int)(_flushIntervalSeconds * 1000);
            _flushTimer = new Timer(OnTimerElapsed, null, intervalMs, intervalMs);
        }

        public void RecordEvaluation(
            string flagKey,
            FlagAssignment assignment,
            FlagsEvaluationContext evaluationContext,
            string flagError)
        {
            // Quick non-locking check to skip expensive work if already disposed.
            // The definitive check happens inside the lock below.
            if (_disposed)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Defensive copy of context attributes to snapshot the current state.
            IReadOnlyDictionary<string, object> contextCopy = evaluationContext?.Attributes != null
                ? new Dictionary<string, object>(evaluationContext.Attributes)
                : null;

            var key = new AggregationKey(
                flagKey: flagKey,
                variantKey: assignment?.VariationKey,
                allocationKey: assignment?.AllocationKey,
                targetingKey: evaluationContext?.TargetingKey,
                errorMessage: flagError,
                context: contextCopy);

            List<FlagEvaluationEvent> eventsToFlush = null;

            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                if (_aggregations.TryGetValue(key, out var existing))
                {
                    existing.EvaluationCount += 1;
                    existing.LastEvaluation = now;
                }
                else
                {
                    var reason = assignment?.Reason;
                    var runtimeDefaultUsed = reason == ReasonDefault || flagError != null;

                    _aggregations[key] = new AggregatedEvaluation(
                        flagKey: flagKey,
                        variantKey: assignment?.VariationKey,
                        allocationKey: assignment?.AllocationKey,
                        targetingKey: evaluationContext?.TargetingKey,
                        targetingRuleKey: null,
                        errorMessage: flagError,
                        context: contextCopy,
                        firstEvaluation: now,
                        runtimeDefaultUsed: runtimeDefaultUsed ? true : (bool?)null);
                }

                if (_aggregations.Count >= _maxAggregations)
                {
                    eventsToFlush = CollectAndClearEvents();
                }
            }

            if (eventsToFlush != null)
            {
                _onFlush?.Invoke(eventsToFlush);
            }
        }

        public void Flush()
        {
            List<FlagEvaluationEvent> events;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                events = CollectAndClearEvents();
            }

            if (events != null)
            {
                _onFlush?.Invoke(events);
            }
        }

        public void Dispose()
        {
            List<FlagEvaluationEvent> events;
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _flushTimer?.Dispose();
                _flushTimer = null;

                events = CollectAndClearEvents();
            }

            if (events != null)
            {
                _onFlush?.Invoke(events);
            }
        }

        // Must be called within _lock.
        private List<FlagEvaluationEvent> CollectAndClearEvents()
        {
            if (_aggregations.Count == 0)
            {
                return null;
            }

            var events = _aggregations.Values
                .Select(a => a.ToFlagEvaluationEvent())
                .ToList();

            _aggregations.Clear();
            return events;
        }

        private void OnTimerElapsed(object state)
        {
            if (_mainThreadContext == null)
            {
                // No main thread synchronization context is available; automatic flushing
                // is disabled to avoid invoking Unity APIs from a timer thread. In this case,
                // callers must invoke Flush() explicitly from the Unity main thread.
                return;
            }

            _mainThreadContext.Post(_ => Flush(), null);
        }
    }
}
