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
    /// <code>
    /// // Setup
    /// DdFlags.Enable(new FlagsConfiguration());
    /// var client = DdFlags.CreateClient();
    /// DdFlags.SetEvaluationContext(new FlagsEvaluationContext("user-123"), onComplete: success =>
    /// {
    ///     // Evaluate flags
    ///     var showFeature = client.GetBooleanValue("show-new-feature", false);
    /// });
    /// </code>
    /// </summary>
    public static class DdFlags
    {
        private static readonly object _lock = new();
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
            lock (_lock)
            {
                if (_enabled)
                {
                    // Already enabled, ignoring
                    return;
                }

                _configuration = configuration ?? new FlagsConfiguration();
                _enabled = true;

                var options = DatadogConfigurationOptions.Load();
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
                    logger: null);
            }
        }

        /// <summary>
        /// Creates a flags client for the given name. Must be called before SetEvaluationContext.
        /// </summary>
        /// <param name="name">A unique name for this client. Defaults to "default".</param>
        public static FlagsClient CreateClient(string name = FlagsClient.DefaultName)
        {
            lock (_lock)
            {
                if (!_enabled)
                {
                    // Not enabled - should call Enable() first
                    return null;
                }

                if (_clients.ContainsKey(name))
                {
                    // Client already exists, return existing
                    return _clients[name];
                }

                var options = DatadogConfigurationOptions.Load();
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
                    logger: null);

                var client = new FlagsClient(
                    repository: repository,
                    exposureTracker: exposureTracker,
                    evaluationAggregator: evaluationAggregator,
                    fetcher: fetcher,
                    logger: null,
                    trackExposures: config.TrackExposures,
                    trackEvaluations: config.TrackEvaluations,
                    onExposure: onExposure);

                _clients[name] = client;
                return client;
            }
        }

        /// <summary>
        /// Sets the evaluation context and fetches precomputed flag assignments from the server.
        /// After the callback fires with success, flags are available via the FlagsClient API.
        /// </summary>
        /// <param name="context">The evaluation context containing targeting key and attributes.</param>
        /// <param name="onComplete">Optional callback invoked when the fetch completes (true = success).</param>
        /// <param name="clientName">The client name. Defaults to "default".</param>
        public static void SetEvaluationContext(
            FlagsEvaluationContext context,
            Action<bool> onComplete = null,
            string clientName = FlagsClient.DefaultName)
        {
            FlagsClient client;
            lock (_lock)
            {
                if (!_clients.TryGetValue(clientName, out client))
                {
                    // No FlagsClient named '{clientName}'. Call DdFlags.CreateClient() first.
                    onComplete?.Invoke(false);
                    return;
                }
            }

            client.SetEvaluationContext(context, onComplete);
        }

        internal static FlagsClient GetClient(string name = FlagsClient.DefaultName)
        {
            lock (_lock)
            {
                _clients.TryGetValue(name, out var client);
                return client;
            }
        }

        /// <summary>
        /// Shuts down the Flags feature and disposes all clients.
        /// </summary>
        public static void Shutdown()
        {
            List<FlagsClient> clientsToDispose;
            lock (_lock)
            {
                clientsToDispose = new List<FlagsClient>(_clients.Values);
                _clients.Clear();
                _telemetrySender = null;
                _configuration = null;
                _enabled = false;
            }

            foreach (var client in clientsToDispose)
            {
                client.Dispose();
            }
        }
    }
}
