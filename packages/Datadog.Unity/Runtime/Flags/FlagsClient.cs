// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Client for evaluating feature flags. Use <c>DdFlags.CreateClient()</c> to create
    /// an instance after calling <c>DdFlags.Enable()</c>.
    /// </summary>
    public class FlagsClient : IFlagsClient
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
        private readonly IFlagsCacheWriter _cacheWriter;
        private readonly IFlagsCacheReader _cacheReader;

        private FlagsClientState _state;
        private bool _disposed;

        internal FlagsClient(
            FlagsRepository repository,
            ExposureTracker exposureTracker,
            EvaluationAggregator evaluationAggregator,
            PrecomputeAssignmentsFetcher fetcher,
            Core.IInternalLogger logger,
            bool trackExposures,
            bool trackEvaluations,
            Action<ExposureEvent> onExposure,
            IFlagsCacheWriter cacheWriter = null,
            IFlagsCacheReader cacheReader = null,
            FlagsClientState initialState = FlagsClientState.NotReady)
        {
            if (trackExposures && exposureTracker == null)
            {
                throw new ArgumentNullException(nameof(exposureTracker),
                    "exposureTracker must not be null when trackExposures is true.");
            }

            _repository = repository;
            _exposureTracker = exposureTracker;
            _evaluationAggregator = evaluationAggregator;
            _fetcher = fetcher;
            _logger = logger;
            _trackExposures = trackExposures;
            _trackEvaluations = trackEvaluations;
            _onExposure = onExposure;
            _cacheWriter = cacheWriter;
            _cacheReader = cacheReader;
            _state = initialState;
            BootstrapFromCache();
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

        private event EventHandler<FlagsStateChange> _stateChanged;

        /// <summary>
        /// Subscribes to client state changes. The handler is invoked immediately with the
        /// current state (where <c>Old == New</c>) so that late subscribers never miss the
        /// initial state, and then on every subsequent transition.
        /// </summary>
        public event EventHandler<FlagsStateChange> StateChanged
        {
            add
            {
                FlagsClientState currentState;
                lock (_lock)
                {
                    _stateChanged += value;
                    currentState = _state;
                }

                // Replay current state to the new subscriber. Old == New signals this is a
                // replay rather than a real transition.
                try
                {
                    value?.Invoke(this, new FlagsStateChange(currentState, currentState));
                }
                catch (Exception ex)
                {
                    _logger?.Log(Logs.DdLogLevel.Warn,
                        $"StateChanged subscriber threw during initial replay: {ex.Message}");
                }
            }
            remove
            {
                lock (_lock)
                {
                    _stateChanged -= value;
                }
            }
        }

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

            if (string.IsNullOrEmpty(context.TargetingKey))
            {
                _logger?.Log(Logs.DdLogLevel.Warn,
                    "SetEvaluationContext called with an empty targeting key. " +
                    "Proceeding with the fetch, but the assignments endpoint will likely return an error. " +
                    "Provide a stable, unique targeting key per user (e.g. user ID).");
            }

            TransitionState(FlagsClientState.Reconciling);

            _fetcher.Fetch(context, (rawJson, flags) =>
            {
                if (flags != null)
                {
                    _cacheWriter?.Write(rawJson, context);
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
                FlagsClientState state;
                lock (_lock)
                {
                    state = _state;
                }

                if (state == FlagsClientState.NotReady || state == FlagsClientState.Reconciling)
                {
                    TrackEvaluation(key, null, "PROVIDER_NOT_READY");
                    return new FlagDetails<T>(key, defaultValue, error: FlagEvaluationError.ProviderNotReady);
                }

                TrackEvaluation(key, null, "FLAG_NOT_FOUND");
                return new FlagDetails<T>(key, defaultValue, error: FlagEvaluationError.FlagNotFound);
            }

            if (!assignment.TryGetValue<T>(out var value, flagKey: key, logger: _logger))
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
            // Guard: skip exposure dispatch when context is null (e.g., bootstrapped from cache
            // before SetEvaluationContext is called). Firing an exposure with an empty subject ID
            // would corrupt server-side attribution data.
            if (_trackExposures && context != null && assignment != null && assignment.DoLog && flagError == null)
            {
                var exposureKey = new ExposureTracker.ExposureKey(
                    targetingKey: context?.TargetingKey ?? string.Empty,
                    flagKey: key,
                    allocationKey: assignment.AllocationKey,
                    variationKey: assignment.VariationKey);

                if (_exposureTracker.TrackExposure(exposureKey))
                {
                    var exposureEvent = new ExposureEvent(
                        timestamp: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        flag: new FlagRef(key),
                        allocation: new FlagRef(assignment.AllocationKey),
                        variant: new FlagRef(assignment.VariationKey),
                        subject: new ExposureSubject(
                            id: context?.TargetingKey ?? string.Empty,
                            attributes: context?.Attributes?.Count > 0 ? context.Attributes : null));

                    _onExposure?.Invoke(exposureEvent);
                }
            }

            // Evaluation aggregation
            if (_trackEvaluations)
            {
                _evaluationAggregator?.RecordEvaluation(key, assignment, context, flagError);
            }
        }

        private void BootstrapFromCache()
        {
            if (_cacheReader == null) return;

            var envelope = _cacheReader.Read(null);
            if (envelope == null)
            {
                _logger?.Log(Logs.DdLogLevel.Debug, "[Flags] No cached flags found.");
                return;
            }

            if (string.IsNullOrEmpty(envelope.Payload))
            {
                _logger?.Log(Logs.DdLogLevel.Debug, "[Flags] Cached payload is empty — skipping bootstrap.");
                return;
            }

            var flags = PrecomputeAssignmentsFetcher.ParseResponse(envelope.Payload);
            if (flags == null)
            {
                _logger?.Log(Logs.DdLogLevel.Debug, "[Flags] Cached payload parse failed — skipping bootstrap.");
                return;
            }

            // Seed repository with restored context from cache envelope. Context is non-null when the
            // envelope was written by a previous SetEvaluationContext call, enabling exposure tracking
            // immediately after bootstrap.
            FlagsEvaluationContext restoredContext = null;
            if (envelope.Context != null && !string.IsNullOrEmpty(envelope.Context.TargetingKey))
            {
                // The DTO stores attributes as Dictionary<string,string> (already flattened by Write).
                // The public constructor accepts Dictionary<string,object>, so we cast each value to
                // object. The constructor then calls Flatten which immediately calls .ToString() on each
                // object value — a no-op round-trip for strings. The data is correct: the cached
                // attributes were already flattened at write time, and the re-flatten on a dict with
                // no nested objects is harmless. An internal bypass constructor was considered but
                // deferred to keep the change scope narrow.
                var attrs = envelope.Context.Attributes?.Count > 0
                    ? envelope.Context.Attributes
                          .ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
                    : null;
                restoredContext = new FlagsEvaluationContext(envelope.Context.TargetingKey, attrs);
            }

            _repository.SetFlagsAndContext(restoredContext, flags);

            // Direct field write, not TransitionState — no subscribers yet at construction time
            // (see RESEARCH.md Pitfall 1). Lock for memory model consistency with every other
            // _state write.
            lock (_lock)
            {
                _state = FlagsClientState.Ready;
            }
            _logger?.Log(Logs.DdLogLevel.Debug, "[Flags] Bootstrapped from cache.");
        }

        private void TransitionState(FlagsClientState newState)
        {
            EventHandler<FlagsStateChange> handler;
            FlagsClientState oldState;
            lock (_lock)
            {
                if (_state == newState)
                {
                    return;
                }

                oldState = _state;
                _state = newState;
                handler = _stateChanged;
            }

            if (handler == null)
            {
                return;
            }

            var args = new FlagsStateChange(oldState, newState);
            foreach (var subscriber in handler.GetInvocationList())
            {
                try
                {
                    ((EventHandler<FlagsStateChange>)subscriber)(this, args);
                }
                catch (Exception ex)
                {
                    _logger?.Log(Logs.DdLogLevel.Warn,
                        $"StateChanged subscriber threw: {ex.Message}");
                }
            }
        }
    }
}
