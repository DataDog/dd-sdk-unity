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
    /// Aggregation key dimensions: (flagKey, variantKey, allocationKey, targetingKey).
    /// Flush triggers: timer (10s default), max aggregations (1000).
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

            public AggregationKey(string flagKey, string variantKey, string allocationKey, string targetingKey)
            {
                FlagKey = flagKey;
                VariantKey = variantKey;
                AllocationKey = allocationKey;
                TargetingKey = targetingKey;
            }

            public bool Equals(AggregationKey other)
            {
                return FlagKey == other.FlagKey
                    && VariantKey == other.VariantKey
                    && AllocationKey == other.AllocationKey
                    && TargetingKey == other.TargetingKey;
            }

            public override bool Equals(object obj)
                => obj is AggregationKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(FlagKey, VariantKey, AllocationKey, TargetingKey);
        }

        internal class AggregatedEvaluation
        {
            public readonly string FlagKey;
            public readonly string VariantKey;
            public readonly string AllocationKey;
            public readonly string TargetingKey;
            public readonly string TargetingRuleKey;
            public readonly IReadOnlyDictionary<string, string> Context;
            public readonly long FirstEvaluation;
            public readonly bool? RuntimeDefaultUsed;

            // Mutable: updated on each subsequent evaluation for the same dimensions.
            public long LastEvaluation;
            public int EvaluationCount;
            public string ErrorMessage;

            public AggregatedEvaluation(
                string flagKey,
                string variantKey,
                string allocationKey,
                string targetingKey,
                string targetingRuleKey,
                string errorMessage,
                IReadOnlyDictionary<string, string> context,
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
                flag: new FlagRef(FlagKey),
                firstEvaluation: FirstEvaluation,
                lastEvaluation: LastEvaluation,
                evaluationCount: EvaluationCount,
                variant: RuntimeDefaultUsed != true && VariantKey != null ? new FlagRef(VariantKey) : null,
                allocation: RuntimeDefaultUsed != true && AllocationKey != null ? new FlagRef(AllocationKey) : null,
                targetingRule: TargetingRuleKey != null ? new FlagRef(TargetingRuleKey) : null,
                targetingKey: TargetingKey,
                runtimeDefaultUsed: RuntimeDefaultUsed,
                error: ErrorMessage != null ? new FlagErrorDetail(ErrorMessage) : null,
                context: Context?.Count > 0 ? new EvaluationContextPayload(Context) : null);
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

            // Only start the timer when a main-thread context is available.
            // When null, automatic flushing is disabled and callers must invoke Flush() explicitly.
            if (_mainThreadContext != null)
            {
                var intervalMs = (int)(_flushIntervalSeconds * 1000);
                _flushTimer = new Timer(OnTimerElapsed, null, intervalMs, intervalMs);
            }
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

            // FlagsEvaluationContext.Attributes is already an immutable ReadOnlyDictionary —
            // no defensive copy needed.
            var contextCopy = evaluationContext?.Attributes;

            var key = new AggregationKey(
                flagKey: flagKey,
                variantKey: assignment?.VariationKey,
                allocationKey: assignment?.AllocationKey,
                targetingKey: evaluationContext?.TargetingKey);

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
                    existing.ErrorMessage = flagError ?? existing.ErrorMessage;
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
