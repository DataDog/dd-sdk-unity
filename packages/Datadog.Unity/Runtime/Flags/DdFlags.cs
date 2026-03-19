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
    /// // Setup
    /// DdFlags.Enable(new FlagsConfiguration());
    /// var client = DdFlags.Instance.CreateClient();
    /// client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), onComplete: success =>
    /// {
    ///     // Evaluate flags
    ///     var showFeature = client.GetBooleanValue("show-new-feature", false);
    /// });
    /// </code>
    /// </summary>
    public class DdFlags
    {
        private static readonly object _enableLock = new();

        private readonly FlagsConfiguration _configuration;
        private readonly EvpTelemetrySender _telemetrySender;
        private readonly IInternalLogger _logger;
        private readonly Dictionary<string, FlagsClient> _clients = new();
        private readonly object _lock = new();

        /// <summary>
        /// Gets the singleton instance of DdFlags. Null until <see cref="Enable"/> is called.
        /// </summary>
        public static DdFlags Instance { get; private set; }

        private DdFlags(FlagsConfiguration configuration, EvpTelemetrySender telemetrySender, IInternalLogger logger)
        {
            _configuration = configuration;
            _telemetrySender = telemetrySender;
            _logger = logger;
        }

        /// <summary>
        /// Enables the Datadog Flags feature and initializes the singleton instance.
        /// Must be called from the Unity main thread after Datadog SDK initialization.
        /// Subsequent calls are ignored.
        /// </summary>
        /// <param name="configuration">Configuration options for the Flags feature.</param>
        public static void Enable(FlagsConfiguration configuration = null)
        {
            lock (_enableLock)
            {
                if (Instance != null)
                {
                    DatadogSdk.Instance?.InternalLogger?.Log(DdLogLevel.Warn,
                        "DdFlags.Enable() called more than once — ignoring.");
                    return;
                }

                if (SynchronizationContext.Current == null)
                {
                    const string message =
                        "DdFlags.Enable() must be called from the Unity main thread. " +
                        "Automatic telemetry flushing will be disabled.";

                    if (Application.isEditor || Debug.isDebugBuild)
                    {
                        throw new InvalidOperationException(message);
                    }
                    else
                    {
                        DatadogSdk.Instance?.InternalLogger?.Log(DdLogLevel.Error, message);
                    }
                }

                configuration ??= new FlagsConfiguration();

                var options = DatadogConfigurationOptions.Load();
                var site = options?.Site ?? DatadogSite.Us1;
                var exposureEndpoint = !string.IsNullOrEmpty(configuration.CustomExposureEndpoint)
                    ? configuration.CustomExposureEndpoint
                    : FlagsEndpoints.GetExposureEndpoint(site);
                var evaluationEndpoint = !string.IsNullOrEmpty(configuration.CustomEvaluationEndpoint)
                    ? configuration.CustomEvaluationEndpoint
                    : FlagsEndpoints.GetEvaluationEndpoint(site);
                var logger = DatadogSdk.Instance?.InternalLogger;

                var sender = new EvpTelemetrySender(
                    clientToken: options?.ClientToken ?? string.Empty,
                    exposureEndpoint: exposureEndpoint,
                    evaluationEndpoint: evaluationEndpoint,
                    env: options?.Env ?? string.Empty,
                    logger: logger);

                Instance = new DdFlags(configuration, sender, logger);
            }
        }

        /// <summary>
        /// Shuts down the Flags feature, disposes all clients, and clears the singleton instance.
        /// </summary>
        public static void Shutdown()
        {
            DdFlags instance;
            lock (_enableLock)
            {
                instance = Instance;
                Instance = null;
            }

            instance?.ShutdownInternal();
        }

        /// <summary>
        /// Creates a flags client for the given name.
        /// </summary>
        /// <param name="name">A unique name for this client. Defaults to "default".</param>
        public FlagsClient CreateClient(string name = FlagsClient.DefaultName)
        {
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

                var repository = new FlagsRepository();
                var exposureTracker = new ExposureTracker();

                Action<ExposureEvent> onExposure = null;
                EvaluationAggregator evaluationAggregator = null;
                if (_telemetrySender != null)
                {
                    if (_configuration.TrackExposures)
                    {
                        onExposure = _telemetrySender.SendExposure;
                    }

                    if (_configuration.TrackEvaluations)
                    {
                        var sender = _telemetrySender;
                        evaluationAggregator = new EvaluationAggregator(
                            onFlush: events => sender.SendEvaluations(events),
                            flushIntervalSeconds: _configuration.EvaluationFlushIntervalSeconds);
                    }
                }

                var fetcher = new PrecomputeAssignmentsFetcher(
                    endpointUrl: precomputeEndpoint,
                    clientToken: options?.ClientToken ?? string.Empty,
                    applicationId: options?.RumApplicationId,
                    env: options?.Env ?? string.Empty,
                    logger: _logger);

                var client = new FlagsClient(
                    repository: repository,
                    exposureTracker: exposureTracker,
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

        internal FlagsClient GetClient(string name = FlagsClient.DefaultName)
        {
            lock (_lock)
            {
                _clients.TryGetValue(name, out var client);
                return client;
            }
        }

        private void ShutdownInternal()
        {
            List<FlagsClient> clientsToDispose;
            lock (_lock)
            {
                clientsToDispose = new List<FlagsClient>(_clients.Values);
                _clients.Clear();
            }

            foreach (var client in clientsToDispose)
            {
                client.Dispose();
            }
        }
    }
}
