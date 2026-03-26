// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Threading;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using UnityEngine;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Entry point for the Datadog Flags feature in Unity.
    ///
    /// <code>
    /// DdFlags.Enable(new FlagsConfiguration());
    /// var client = DdFlags.Instance.CreateClient();
    /// client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), onComplete: success =>
    /// {
    ///     var showFeature = client.GetBooleanValue("show-new-feature", false);
    /// });
    /// </code>
    /// </summary>
    public class DdFlags
    {
        private static readonly object _enableLock = new();

        /// <summary>
        /// The singleton. Always non-null. Call <see cref="Enable"/> to configure it.
        /// Before <see cref="Enable"/> succeeds, <see cref="CreateClient"/> returns a
        /// <see cref="NoopFlagsClient"/> with reason <c>"DEFAULT"</c>.
        /// </summary>
        public static readonly DdFlags Instance = new();

        private FlagsConfiguration _configuration;
        private EvpTelemetrySender _telemetrySender;
        private IInternalLogger _logger;
        private volatile bool _isEnabled;
        private readonly Dictionary<string, IFlagsClient> _clients = new();
        private readonly object _lock = new();

        private DdFlags() { }

        /// <summary>
        /// Enables the Datadog Flags feature. Must be called from the Unity main thread
        /// after Datadog SDK initialization. Subsequent calls are ignored.
        /// </summary>
        public static void Enable(FlagsConfiguration configuration = null)
        {
            lock (_enableLock)
            {
                if (Instance._isEnabled)
                {
                    DatadogSdk.Instance?.InternalLogger?.Log(DdLogLevel.Warn,
                        "DdFlags.Enable() called more than once — ignoring.");
                    return;
                }

                var logger = DatadogSdk.Instance?.InternalLogger;

                if (SynchronizationContext.Current == null)
                {
                    ReportMisconfiguration(logger,
                        "DdFlags.Enable() must be called from the Unity main thread. " +
                        "Automatic telemetry flushing will be disabled.");
                    // Non-fatal: continue in degraded mode without automatic flushing.
                }

                configuration ??= new FlagsConfiguration();

                var options = DatadogConfigurationOptions.Load();

                if (string.IsNullOrEmpty(options?.Env))
                {
                    ReportMisconfiguration(logger,
                        "DdFlags.Enable() requires the Datadog 'Env' setting to be configured. " +
                        "Set Env in your DatadogSettings asset. " +
                        "Flag evaluations will return default values.");
                    return;
                }

                if (string.IsNullOrEmpty(options?.ClientToken))
                {
                    ReportMisconfiguration(logger,
                        "DdFlags.Enable() requires the Datadog 'ClientToken' setting to be configured. " +
                        "Set ClientToken in your DatadogSettings asset. " +
                        "Flag evaluations will return default values.");
                    return;
                }

                Instance.Configure(configuration, logger);
            }
        }

        /// <summary>
        /// Shuts down the Flags feature, disposes all clients, and resets to pre-Enable state.
        /// </summary>
        public static void Shutdown()
        {
            lock (_enableLock)
            {
                // Only flip _isEnabled — do not null out _configuration/_telemetrySender/_logger.
                // Nulling those fields under a different lock than CreateClient() uses (_lock)
                // creates a race where a concurrent CreateClient() could observe _isEnabled == true
                // and then hit NullReferenceException on _configuration.
                Instance._isEnabled = false;
            }

            Instance.ShutdownInternal();
        }

        /// <summary>
        /// Creates a flags client for the given name. Returns a <see cref="NoopFlagsClient"/>
        /// if the SDK has not been enabled yet.
        /// </summary>
        public IFlagsClient CreateClient(string name = FlagsClient.DefaultName)
        {
            if (!_isEnabled)
            {
                return new NoopFlagsClient("DEFAULT", DatadogSdk.Instance?.InternalLogger);
            }

            lock (_lock)
            {
                if (_clients.TryGetValue(name, out var existingClient))
                {
                    return existingClient;
                }

                var options = DatadogConfigurationOptions.Load();

                string precomputeEndpoint;
                if (!string.IsNullOrEmpty(_configuration.CustomFlagsEndpoint))
                {
                    precomputeEndpoint = _configuration.CustomFlagsEndpoint;
                }
                else if (options != null)
                {
                    precomputeEndpoint = FlagsEndpoints.GetPrecomputeEndpoint(options.Site);
                }
                else
                {
                    precomputeEndpoint = FlagsEndpoints.GetPrecomputeEndpoint(DatadogSite.Us1);
                }

                var sender = GetOrCreateSender(options);

                Action<ExposureEvent> onExposure = null;
                EvaluationAggregator evaluationAggregator = null;
                if (_configuration.TrackExposures)
                {
                    onExposure = sender.SendExposure;
                }

                if (_configuration.TrackEvaluations)
                {
                    evaluationAggregator = new EvaluationAggregator(
                        onFlush: events => sender.SendEvaluations(events),
                        flushIntervalSeconds: _configuration.EvaluationFlushIntervalSeconds);
                }

                var fetcher = new PrecomputeAssignmentsFetcher(
                    endpointUrl: precomputeEndpoint,
                    clientToken: options?.ClientToken ?? string.Empty,
                    applicationId: options?.RumApplicationId,
                    env: options?.Env ?? string.Empty,
                    logger: _logger);

                var client = new FlagsClient(
                    repository: new FlagsRepository(),
                    exposureTracker: new ExposureTracker(),
                    evaluationAggregator: evaluationAggregator,
                    fetcher: fetcher,
                    logger: _logger,
                    trackExposures: _configuration.TrackExposures,
                    trackEvaluations: _configuration.TrackEvaluations,
                    onExposure: onExposure);

                _clients[name] = client;
                return client;
            }
        }

        internal IFlagsClient GetClient(string name = FlagsClient.DefaultName)
        {
            lock (_lock)
            {
                _clients.TryGetValue(name, out var client);
                return client;
            }
        }

        // Sets configuration and marks the instance as enabled.
        private void Configure(FlagsConfiguration configuration, IInternalLogger logger)
        {
            _configuration = configuration;
            _logger = logger;
            _isEnabled = true;
        }

        // Lazily creates the shared telemetry sender; reuses it on subsequent calls.
        private EvpTelemetrySender GetOrCreateSender(DatadogConfigurationOptions options)
        {
            if (_telemetrySender != null)
            {
                return _telemetrySender;
            }

            var site = options?.Site ?? DatadogSite.Us1;
            var exposureEndpoint = !string.IsNullOrEmpty(_configuration.CustomExposureEndpoint)
                ? _configuration.CustomExposureEndpoint
                : FlagsEndpoints.GetExposureEndpoint(site);
            var evaluationEndpoint = !string.IsNullOrEmpty(_configuration.CustomEvaluationEndpoint)
                ? _configuration.CustomEvaluationEndpoint
                : FlagsEndpoints.GetEvaluationEndpoint(site);

            _telemetrySender = new EvpTelemetrySender(
                clientToken: options?.ClientToken ?? string.Empty,
                exposureEndpoint: exposureEndpoint,
                evaluationEndpoint: evaluationEndpoint,
                env: options?.Env ?? string.Empty,
                logger: _logger);

            return _telemetrySender;
        }

        // Reports a misconfiguration: throws in editor/debug builds so developers catch it
        // immediately; logs at error level in production so the app never crashes.
        private static void ReportMisconfiguration(IInternalLogger logger, string message)
        {
            if (Application.isEditor || Debug.isDebugBuild)
            {
                throw new InvalidOperationException(message);
            }

            logger?.Log(DdLogLevel.Error, message);
        }

        private void ShutdownInternal()
        {
            List<IFlagsClient> clientsToDispose;
            lock (_lock)
            {
                clientsToDispose = new List<IFlagsClient>(_clients.Values);
                _clients.Clear();
            }

            foreach (var client in clientsToDispose)
            {
                client.Dispose();
            }
        }
    }
}
