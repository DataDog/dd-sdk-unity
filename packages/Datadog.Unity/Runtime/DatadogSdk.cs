// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Datadog.Unity
{
    public class DatadogSdk
    {
        public static readonly DatadogSdk Instance = new();

        private IDatadogPlatform _platform = new DatadogNoOpPlatform();

        private DdUnityLogHandler _logHandler;
        private DatadogWorker _worker;
        private IInternalLogger _internalLogger = new PassThroughInternalLogger();
        private ResourceTrackingHelper _resourceTrackingHelper;
        private GameObject _performanceTrackerObject;

        /// <summary>
        /// Gets the version of the SDK reported to Datadog. This strips the final (revision) part of the version.
        /// to be more compliant with "SemVer" standards.
        /// </summary>
        public static string SdkVersion
        {
            get
            {
                var version = typeof(DatadogSdk).Assembly.GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
            }
        }

        private DatadogSdk()
        {
            DefaultLogger = new DdNoOpLogger();
        }

        /// <summary>
        /// The default logger. This logger is used when intercepting Unity logs.
        /// </summary>
        public DdLogger DefaultLogger
        {
            get; private set;
        }

        /// <summary>
        /// The instance of the RUM feature of the Datadog SDK. If RUM is not enabled,
        /// this property returns a NoOp implementation of the RUM interface, and will never return null.
        /// </summary>
        public IDdRum Rum { get; private set; } = new DdNoOpRum();

        internal IInternalLogger InternalLogger
        {
            get { return _internalLogger;  }
            set
            {
                _internalLogger = value ?? new PassThroughInternalLogger();
            }
        }

        /// <summary>
        /// Provides access to the logic used to generate and inject trace context. If you're using
        /// <see cref="DatadogTrackedWebRequest"/>, HTTP requests are tracked automatically. Use
        /// ResourceTrackingHelper only if you need lower-level control, e.g. to manually inject
        /// trace context into requests made with a different HTTP client implementation.
        /// </summary>
        public ResourceTrackingHelper ResourceTrackingHelper => _resourceTrackingHelper;

        /// <summary>
        /// Shutdown the Datadog SDK. Note, this method is primarily for internal use.
        /// </summary>
        public static void Shutdown()
        {
            Instance.ShutdownInstance(false);
        }

        /// <summary>
        /// Sets the verbosity level of the SDK. This will affect the amount of logs that the SDK will output.
        /// </summary>
        /// <param name="logLevel">The level of SDK verbosity.</param>
        public void SetSdkVerbosity(CoreLoggerLevel logLevel)
        {
            InternalHelpers.Wrap("SetSdkVerbosity", () =>
            {
                _platform.SetVerbosity(logLevel);
            });
        }

        /// <summary>
        /// Sets the tracking consent regarding the data collection for this instance of the Datadog SDK.
        ///
        /// Datadog always defaults to TrackingConsent.Pending, and it is expected that you call this method after
        /// asking the end user for their consent to track data.
        /// </summary>
        /// <param name="trackingConsent">The current value for tracking consent.</param>
        public void SetTrackingConsent(TrackingConsent trackingConsent)
        {
            InternalHelpers.Wrap("SetTrackingConsent", () =>
            {
                _platform.SetTrackingConsent(trackingConsent);
            });
        }

        /// <summary>
        /// Sets information about the current user. User information will be added to logs, traces, and RUM events
        /// automatically.
        /// </summary>
        /// <param name="id">The ID for the user.</param>
        /// <param name="name">The name for the user.</param>
        /// <param name="email">The user's email.</param>
        /// <param name="extraInfo">A map of any extra information about the user.</param>
        public void SetUserInfo(
            string id = null,
            string name = null,
            string email = null,
            Dictionary<string, object> extraInfo = null)
        {
            InternalHelpers.Wrap("SetUserInfo", () =>
            {
                _worker?.AddMessage(new DdSdkProcessor.SetUserInfoMessage(id, name, email, extraInfo));
            });
        }

        /// <summary>
        /// Add custom attributes to the current user information
        ///
        /// This extra info will be added to already existing extra info that is added to logs, traces, and RUM events
        /// automatically.
        ///
        /// Setting an existing attribute to `null` will remove that attribute from the user's extra info.
        /// </summary>
        /// <param name="extraInfo">Any additional extra info about a user.</param>
        public void AddUserExtraInfo(Dictionary<string, object> extraInfo)
        {
            InternalHelpers.Wrap("AddUserExtraInfo", () =>
            {
                _worker?.AddMessage(new DdSdkProcessor.AddUserExtraInfoMessage(extraInfo));
            });
        }

        /// <summary>
        /// Create a logger with the given logging options.
        ///
        /// Even if this function fails, it wil not return null, and instead will return a NoOp logger.
        /// </summary>
        /// <param name="options">The options for the logger.</param>
        /// <returns>The requested logger.</returns>
        public DdLogger CreateLogger(DatadogLoggingOptions options)
        {
            try
            {
                return _platform?.CreateLogger(options, _worker);
            }
            catch (Exception e)
            {
                var internalLogger = DatadogSdk.Instance.InternalLogger;
                internalLogger?.Log(DdLogLevel.Warn, $"Error creating logger: {e}");
                internalLogger?.Log(DdLogLevel.Warn, "A NoOp logger will be used instead.");

                internalLogger?.TelemetryError("Error creating logger", e);

                return new DdNoOpLogger();
            }
        }

        /// <summary>
        /// Add a custom attribute to all future logs sent by all loggers.
        ///
        /// Values can be nested up to 10 levels deep. Keys using more than 10 levels will be sanitized by SDK.
        /// </summary>
        /// <param name="key">The key of the attribute to add.</param>
        /// <param name="value">The value of the attribute.</param>
        public void AddLogsAttribute(string key, object value)
        {
            if (key == null)
            {
                _internalLogger.Log(DdLogLevel.Warn, "Attempting to add `null` key to logs attributes. Ignoring.");
                return;
            }

            InternalHelpers.Wrap("AddLogsAttribute", () =>
            {
                _worker?.AddMessage(DdSdkProcessor.AddGlobalAttributesMessage.Create(new Dictionary<string, object> { { key, value } }));
            });
        }

        /// <summary>
        /// Add multiple custom attribute to all future logs sent by all loggers. This call will replace values
        /// on previous attributes if they exit.
        ///
        /// Values can be nested up to 10 levels deep. Keys using more than 10 levels will be sanitized by SDK.
        /// </summary>
        /// <param name="attributes">A map of custom attributes.</param>
        public void AddLogsAttributes(Dictionary<string, object> attributes)
        {
            InternalHelpers.Wrap("AddLogsAttributes", () =>
            {
                _worker?.AddMessage(DdSdkProcessor.AddGlobalAttributesMessage.Create(attributes));
            });
        }

        /// <summary>
        /// Remove a custom attribute from all future logs sent by all loggers.
        ///
        /// Previous logs won't lose the attribute value associated with this [key] if
        /// they were created prior to this call.
        /// </summary>
        /// <param name="key">The key of the attribute to remove.</param>
        public void RemoveLogsAttribute(string key)
        {
            if (key == null)
            {
                _internalLogger.Log(DdLogLevel.Warn, "Attempting to remove `null` key from logs attributes. Ignoring.");
                return;
            }

            InternalHelpers.Wrap("AddLogsAttributes", () =>
            {
                _worker?.AddMessage(DdSdkProcessor.RemoveGlobalAttributeMessage.Create(key));
            });
        }

        /// <summary>
        /// Clear all data currently stored by the Datadog SDK.
        /// </summary>
        public void ClearAllData()
        {
            InternalHelpers.Wrap("ClearAllData", () =>
            {
                _platform.ClearAllData();
            });
        }

        internal static void InitWithPlatform(IDatadogPlatform platform, DatadogConfigurationOptions options)
        {
            Instance.Init(platform, options);
        }

        private void Init(IDatadogPlatform platform, DatadogConfigurationOptions options)
        {
            try
            {
                _platform = platform;

                // Create our worker thread
                _worker = _platform.CreateWorker();
                _worker.AddProcessor(DdSdkProcessor.SdkTargetName, new DdSdkProcessor(_platform));

                _worker.AddProcessor(DdLogsProcessor.LogsTargetName, new DdLogsProcessor());
                _internalLogger = new InternalLogger(_worker, _platform);
                _resourceTrackingHelper = new ResourceTrackingHelper(options);

                var loggingOptions = new DatadogLoggingOptions()
                {
                    RemoteLogThreshold = DdLogHelpers.LogTypeToDdLogLevel(options.RemoteLogThreshold),
                };
                DefaultLogger = CreateLogger(loggingOptions);

                if (options.RumEnabled)
                {
                    EnableRum(options);
                }

                if (options.ForwardUnityLogs)
                {
                    _logHandler = new (DefaultLogger, options.RumEnabled ? Rum : null);
                    _logHandler.Attach();
                }

                _worker.Start();

                Application.quitting += OnQuitting;
            }
            catch (Exception e)
            {
                _internalLogger?.TelemetryError("Error initializing DatadogSdk", e);
            }
        }

        private void EnableRum(DatadogConfigurationOptions options)
        {
            // Require an application ID
            if (string.IsNullOrEmpty(options.RumApplicationId))
            {
                _internalLogger.Log(DdLogLevel.Error, "Datadog RUM is enabled but an Application ID is not set.");
                return;
            }

            // Prepare our interface to the platform-specific RUM API, and register it to handle worker messages in a
            // background thread
            var platformRum = _platform.InitRum(options);
            _worker.AddProcessor(DdRumProcessor.RumTargetName, new DdRumProcessor(platformRum));

            // Create our main-thread IDdRum interface, which will enqueue messages for the worker-thread implementation
            // to handle
            var rumProxy = new DdWorkerProxyRum(_worker);
            Rum = rumProxy;

            // If we're configured to send performance metrics, instantiate a helper object that will observe frame
            // timings from within the Unity scene
            if (options.VitalsUpdateFrequency != VitalsUpdateFrequency.None)
            {
                // Create a persistent GameObject and add the DatadogPerformanceTracker behavior to it
                _performanceTrackerObject = new GameObject("DatadogPerformanceTracker");
                var performanceTracker = _performanceTrackerObject.AddComponent<DatadogPerformanceTracker>();
                _performanceTrackerObject.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(_performanceTrackerObject);

                // Determine how often we should read from the PerformanceTracker and pass the latest performance
                // metrics into the platform SDK
                float reportIntervalSeconds;
                switch (options.VitalsUpdateFrequency)
                {
                    case VitalsUpdateFrequency.Frequent:
                        reportIntervalSeconds = 0.1f;
                        break;
                    case VitalsUpdateFrequency.Average:
                        reportIntervalSeconds = 0.5f;
                        break;
                    default:
                        reportIntervalSeconds = 1.0f;
                        break;
                }

                // Pass a reference to our performance tracker into the RUM proxy, and configure it to periodically
                // sample the latest values (on the main thread) and initiate updateExternalRefreshRate calls
                rumProxy.InitPerformanceTracker(performanceTracker, reportIntervalSeconds);
            }

            // Track scene changes as RUM Views, if configured to do so
            if (options.AutomaticSceneTracking)
            {
                SceneManager.activeSceneChanged += SceneManagerOnActiveSceneChanged;
            }
        }

        private void SceneManagerOnActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            Rum.StartView(nextScene.path, nextScene.name, new Dictionary<string, object>()
            {
                { "is_sub_scene", nextScene.isSubScene },
                { "is_loaded", nextScene.isLoaded },
            });
        }

        /// <summary>
        /// Cleans up internal state owned by the SDK, either when quitting the application or when tearing down test
        /// state.
        /// </summary>
        /// <param name="isQuitting">Whether we're being called in response to <c>Application.quitting</c>.</param>
        private void ShutdownInstance(bool isQuitting)
        {
            // Clean up any game objects added to the scene by the SDK, but avoid doing so if the application is
            // shutting down: only delete objects in response to manual DatadogSdk.Shutdown() calls, i.e. in unit tests
            if (!isQuitting && _performanceTrackerObject != null)
            {
                UnityEngine.Object.Destroy(_performanceTrackerObject);
                _performanceTrackerObject = null;
            }

            // Always clean up SDK state
            _platform = null;
            DefaultLogger = null;
            _logHandler?.Detach();
            _logHandler = null;
            _worker?.Stop();
            _worker = null;
        }

        private void OnQuitting()
        {
            ShutdownInstance(true);
        }

        internal class ConfigKeys
        {
            internal const string Source = "_dd.source";
            internal const string ErrorSourceType = "_dd.error.source_type";
            internal const string BuildId = "_dd.build_id";
            internal const string SdkVersion = "_dd.sdk_version";
            internal const string NativeSourceType = "_dd.native_source_type";
        }
    }
}
