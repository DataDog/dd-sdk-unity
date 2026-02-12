// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Datadog.Unity.Core;
using OpenFeature;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Entry point for the Datadog Flags feature in Unity.
    /// Registers a Datadog-backed OpenFeature provider automatically.
    ///
    /// <code>
    /// // Setup
    /// DdFlags.Enable(new FlagsConfiguration());
    /// DdFlags.CreateClient();
    /// DdFlags.SetEvaluationContext(new FlagsEvaluationContext("user-123"), onComplete: success =>
    /// {
    ///     // Evaluate flags via OpenFeature
    ///     var ofClient = Api.Instance.GetClient();
    ///     var showFeature = await ofClient.GetBooleanValueAsync("show-new-feature", false);
    /// });
    /// </code>
    /// </summary>
    public static class DdFlags
    {
        private static readonly object _lock = new();
        private static FlagsConfiguration _configuration;
        private static EvpTelemetrySender _telemetrySender;
        private static DatadogFeatureProvider _provider;
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
                    DatadogSdk.Instance.InternalLogger?.Log(Logs.DdLogLevel.Warn, "DdFlags.Enable called multiple times. Ignoring.");
                    return;
                }

                _configuration = configuration ?? new FlagsConfiguration();
                _enabled = true;

                // Register OpenFeature provider
                _provider = new DatadogFeatureProvider();
                _ = Api.Instance.SetProviderAsync(_provider).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        DatadogSdk.Instance.InternalLogger?.Log(Logs.DdLogLevel.Warn,
                            $"Failed to set OpenFeature provider: {t.Exception?.GetBaseException()?.Message}");
                    }
                }, TaskScheduler.Default);

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
                        env: options.Env ?? string.Empty,
                        logger: DatadogSdk.Instance.InternalLogger);
                }
                else
                {
                    DatadogSdk.Instance.InternalLogger?.Log(Logs.DdLogLevel.Warn,
                        "DdFlags.Enable: Datadog SDK not configured. Telemetry will be disabled.");
                }
            }
        }

        /// <summary>
        /// Creates a flags client for the given name. Must be called before SetEvaluationContext.
        /// The default client is automatically wired as the OpenFeature provider.
        /// </summary>
        /// <param name="name">A unique name for this client. Defaults to "default".</param>
        public static void CreateClient(string name = FlagsClient.DefaultName)
        {
            lock (_lock)
            {
                if (!_enabled)
                {
                    DatadogSdk.Instance.InternalLogger?.Log(Logs.DdLogLevel.Warn,
                        "DdFlags.CreateClient called before DdFlags.Enable(). Call DdFlags.Enable() first.");
                    return;
                }

                if (_clients.ContainsKey(name))
                {
                    return;
                }

                var options = DatadogConfigurationOptions.Load();
                var logger = DatadogSdk.Instance.InternalLogger;
                var config = _configuration;

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

                // Wire up the default client as the OpenFeature provider's backing client
                if (name == FlagsClient.DefaultName)
                {
                    _provider?.SetClient(client);
                }
            }
        }

        /// <summary>
        /// Sets the evaluation context and fetches precomputed flag assignments from the server.
        /// After the callback fires with success, flags are available via the OpenFeature API.
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
                    DatadogSdk.Instance.InternalLogger?.Log(Logs.DdLogLevel.Warn,
                        $"No FlagsClient named '{clientName}'. Call DdFlags.CreateClient() first.");
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
            DatadogFeatureProvider providerToShutdown;
            lock (_lock)
            {
                clientsToDispose = new List<FlagsClient>(_clients.Values);
                providerToShutdown = _provider;
                _clients.Clear();
                _telemetrySender = null;
                _provider = null;
                _configuration = null;
                _enabled = false;
            }

            // Disconnect the provider so stale OpenFeature calls return ProviderNotReady
            providerToShutdown?.SetClient(null);

            foreach (var client in clientsToDispose)
            {
                client.Dispose();
            }
        }
    }
}
