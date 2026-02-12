// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("com.datadoghq.unity.tests")]

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Internal client for evaluating feature flags. Customers should use the OpenFeature API
    /// via <c>Api.Instance.GetClient()</c> after calling <c>DdFlags.Enable()</c>.
    /// </summary>
    internal class FlagsClient : IDisposable
    {
        public const string DefaultName = "default";

        private readonly object _lock = new();
        private readonly FlagsRepository _repository;
        private readonly ExposureTracker _exposureTracker;
        private readonly EvaluationAggregator _evaluationAggregator;
        private readonly PrecomputeAssignmentsFetcher _fetcher;
        private readonly Core.IInternalLogger _logger;
        private readonly bool _trackExposures;
        private readonly bool _trackEvaluations;
        private readonly Action<ExposureEvent> _onExposure;

        private FlagsClientState _state = FlagsClientState.NotReady;
        private bool _disposed;

        internal FlagsClient(
            FlagsRepository repository,
            ExposureTracker exposureTracker,
            EvaluationAggregator evaluationAggregator,
            PrecomputeAssignmentsFetcher fetcher,
            Core.IInternalLogger logger,
            bool trackExposures,
            bool trackEvaluations,
            Action<ExposureEvent> onExposure)
        {
            _repository = repository;
            _exposureTracker = exposureTracker;
            _evaluationAggregator = evaluationAggregator;
            _fetcher = fetcher;
            _logger = logger;
            _trackExposures = trackExposures;
            _trackEvaluations = trackEvaluations;
            _onExposure = onExposure;
        }

        /// <summary>
        /// Gets the current state of the client.
        /// </summary>
        public FlagsClientState State
        {
            get
            {
                lock (_lock)
                {
                    return _state;
                }
            }
        }

        /// <summary>
        /// Event raised when the client state changes.
        /// </summary>
        public event Action<FlagsClientState> StateChanged;

        /// <summary>
        /// Sets the evaluation context and fetches precomputed flag assignments from the server.
        /// </summary>
        /// <param name="context">The evaluation context containing targeting key and attributes.</param>
        /// <param name="onComplete">Optional callback invoked when the fetch completes (true = success).</param>
        public void SetEvaluationContext(FlagsEvaluationContext context, Action<bool> onComplete = null)
        {
            if (context == null)
            {
                _logger?.Log(Logs.DdLogLevel.Warn, "SetEvaluationContext called with null context. Ignoring.");
                onComplete?.Invoke(false);
                return;
            }

            TransitionState(FlagsClientState.Reconciling);

            _fetcher.Fetch(context, flags =>
            {
                if (flags != null)
                {
                    _repository.SetFlagsAndContext(context, flags);
                    TransitionState(FlagsClientState.Ready);
                    onComplete?.Invoke(true);
                }
                else
                {
                    // If we have cached flags, transition to Stale; otherwise Error
                    if (_repository.HasFlags())
                    {
                        TransitionState(FlagsClientState.Stale);
                    }
                    else
                    {
                        TransitionState(FlagsClientState.Error);
                    }
                    onComplete?.Invoke(false);
                }
            });
        }

        // --- Type-safe value accessors ---

        public bool GetBooleanValue(string key, bool defaultValue)
        {
            return GetValue(key, defaultValue);
        }

        public string GetStringValue(string key, string defaultValue)
        {
            return GetValue(key, defaultValue);
        }

        public int GetIntegerValue(string key, int defaultValue)
        {
            return GetValue(key, defaultValue);
        }

        public double GetDoubleValue(string key, double defaultValue)
        {
            return GetValue(key, defaultValue);
        }

        public object GetObjectValue(string key, object defaultValue)
        {
            return GetValue(key, defaultValue);
        }

        // --- Detailed accessors ---

        public FlagDetails<bool> GetBooleanDetails(string key, bool defaultValue)
        {
            return GetDetails(key, defaultValue);
        }

        public FlagDetails<string> GetStringDetails(string key, string defaultValue)
        {
            return GetDetails(key, defaultValue);
        }

        public FlagDetails<int> GetIntegerDetails(string key, int defaultValue)
        {
            return GetDetails(key, defaultValue);
        }

        public FlagDetails<double> GetDoubleDetails(string key, double defaultValue)
        {
            return GetDetails(key, defaultValue);
        }

        /// <summary>
        /// Gets detailed evaluation result for a flag, including variant, reason, and error info.
        /// </summary>
        public FlagDetails<T> GetDetails<T>(string key, T defaultValue)
        {
            var assignment = _repository.GetFlagAssignment(key);

            if (assignment == null)
            {
                TrackEvaluation(key, null, "FLAG_NOT_FOUND");
                return new FlagDetails<T>(key, defaultValue, error: FlagEvaluationError.FlagNotFound);
            }

            if (!assignment.TryGetValue<T>(out var value))
            {
                TrackEvaluation(key, assignment, "TYPE_MISMATCH");
                return new FlagDetails<T>(key, defaultValue, error: FlagEvaluationError.TypeMismatch);
            }

            var details = new FlagDetails<T>(
                key: key,
                value: value,
                variant: assignment.VariationKey,
                reason: assignment.Reason);

            TrackEvaluation(key, assignment, null);
            return details;
        }

        /// <summary>
        /// Flushes any pending aggregated evaluation events.
        /// </summary>
        public void Flush()
        {
            _evaluationAggregator?.Flush();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
            }
            _evaluationAggregator?.Dispose();
        }

        private T GetValue<T>(string key, T defaultValue)
        {
            return GetDetails(key, defaultValue).Value;
        }

        private void TrackEvaluation(string key, FlagAssignment assignment, string flagError)
        {
            var context = _repository.Context;

            // Exposure tracking
            if (_trackExposures && assignment != null && assignment.DoLog && flagError == null)
            {
                var exposureKey = new ExposureTracker.ExposureKey(
                    targetingKey: context?.TargetingKey ?? string.Empty,
                    flagKey: key,
                    allocationKey: assignment.AllocationKey,
                    variationKey: assignment.VariationKey);

                if (_exposureTracker.InsertIfAbsent(exposureKey))
                {
                    var exposureEvent = new ExposureEvent(
                        timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        flagKey: key,
                        allocationKey: assignment.AllocationKey,
                        variationKey: assignment.VariationKey,
                        subjectId: context?.TargetingKey ?? string.Empty,
                        subjectAttributes: context?.Attributes);

                    _onExposure?.Invoke(exposureEvent);
                }
            }

            // Evaluation aggregation
            if (_trackEvaluations)
            {
                _evaluationAggregator?.RecordEvaluation(key, assignment, context, flagError);
            }
        }

        private void TransitionState(FlagsClientState newState)
        {
            Action<FlagsClientState> handler;
            lock (_lock)
            {
                if (_state == newState)
                {
                    return;
                }
                _state = newState;
                handler = StateChanged;
            }
            handler?.Invoke(newState);
        }
    }
}
