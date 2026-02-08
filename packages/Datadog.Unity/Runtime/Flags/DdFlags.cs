// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Core;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Entry point for the Datadog Flags feature in Unity.
    ///
    /// Usage:
    /// <code>
    /// // After Datadog SDK is initialized
    /// DdFlags.Enable(new FlagsConfiguration());
    ///
    /// // Create a client
    /// var client = DdFlags.CreateClient();
    ///
    /// // Set evaluation context
    /// client.SetEvaluationContext(new FlagsEvaluationContext("user-123", new Dictionary&lt;string, object&gt;
    /// {
    ///     { "email", "user@example.com" },
    ///     { "plan", "premium" }
    /// }));
    ///
    /// // Evaluate flags
    /// var showFeature = client.GetBooleanValue("show-new-feature", false);
    /// </code>
    /// </summary>
    public static class DdFlags
    {
        private static FlagsConfiguration _configuration;
        private static EvpTelemetrySender _telemetrySender;
        private static readonly Dictionary<string, FlagsClient> _clients = new();
        private static bool _enabled;

        /// <summary>
        /// Enables the Datadog Flags feature. Must be called after Datadog SDK initialization.
        /// </summary>
        /// <param name="configuration">Configuration options for the Flags feature.</param>
        public static void Enable(FlagsConfiguration configuration = null)
        {
            if (_enabled)
            {
                DatadogSdk.Instance.InternalLogger?.Log(Logs.DdLogLevel.Warn, "DdFlags.Enable called multiple times. Ignoring.");
                return;
            }

            _configuration = configuration ?? new FlagsConfiguration();
            _enabled = true;

            // Initialize the telemetry sender
            var options = DatadogConfigurationOptions.Load();
            if (options != null)
            {
                var exposureEndpoint = FlagsEndpoints.GetExposureEndpoint(options.Site, _configuration.CustomExposureEndpoint);
                var evaluationEndpoint = FlagsEndpoints.GetEvaluationEndpoint(options.Site, _configuration.CustomEvaluationEndpoint);

                _telemetrySender = new EvpTelemetrySender(
                    clientToken: options.ClientToken,
                    exposureEndpoint: exposureEndpoint,
                    evaluationEndpoint: evaluationEndpoint,
                    logger: DatadogSdk.Instance.InternalLogger);
            }
        }

        /// <summary>
        /// Creates a new FlagsClient for evaluating feature flags.
        /// </summary>
        /// <param name="name">A unique name for this client. Defaults to "default".</param>
        /// <returns>A FlagsClient instance.</returns>
        public static FlagsClient CreateClient(string name = FlagsClient.DefaultName)
        {
            if (!_enabled)
            {
                DatadogSdk.Instance.InternalLogger?.Log(Logs.DdLogLevel.Warn,
                    "DdFlags.CreateClient called before DdFlags.Enable(). Call DdFlags.Enable() first.");
            }

            if (_clients.ContainsKey(name))
            {
                DatadogSdk.Instance.InternalLogger?.Log(Logs.DdLogLevel.Warn,
                    $"FlagsClient named '{name}' already exists. Returning existing client.");
                return _clients[name];
            }

            var options = DatadogConfigurationOptions.Load();
            var logger = DatadogSdk.Instance.InternalLogger;
            var config = _configuration ?? new FlagsConfiguration();

            // Determine precompute endpoint
            string precomputeEndpoint;
            if (!string.IsNullOrEmpty(config.CustomFlagsEndpoint))
            {
                precomputeEndpoint = config.CustomFlagsEndpoint;
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
            if (config.TrackExposures && _telemetrySender != null)
            {
                onExposure = _telemetrySender.SendExposure;
            }

            EvaluationAggregator evaluationAggregator = null;
            if (config.TrackEvaluations && _telemetrySender != null)
            {
                var sender = _telemetrySender;
                evaluationAggregator = new EvaluationAggregator(
                    onFlush: events => sender.SendEvaluations(events),
                    flushIntervalSeconds: config.EvaluationFlushIntervalSeconds);
            }

            var fetcher = new PrecomputeAssignmentsFetcher(
                endpointUrl: precomputeEndpoint,
                clientToken: options?.ClientToken ?? string.Empty,
                applicationId: options?.RumApplicationId,
                env: options?.Env ?? string.Empty,
                logger: logger);

            var client = new FlagsClient(
                repository: repository,
                exposureTracker: exposureTracker,
                evaluationAggregator: evaluationAggregator,
                fetcher: fetcher,
                logger: logger,
                trackExposures: config.TrackExposures,
                trackEvaluations: config.TrackEvaluations,
                onExposure: onExposure);

            _clients[name] = client;
            return client;
        }

        /// <summary>
        /// Gets an existing FlagsClient by name.
        /// </summary>
        /// <param name="name">The name of the client. Defaults to "default".</param>
        /// <returns>The FlagsClient, or null if not found.</returns>
        public static FlagsClient GetClient(string name = FlagsClient.DefaultName)
        {
            _clients.TryGetValue(name, out var client);
            return client;
        }

        /// <summary>
        /// Shuts down the Flags feature and disposes all clients.
        /// </summary>
        public static void Shutdown()
        {
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }
            _clients.Clear();
            _telemetrySender = null;
            _configuration = null;
            _enabled = false;
        }
    }
}
