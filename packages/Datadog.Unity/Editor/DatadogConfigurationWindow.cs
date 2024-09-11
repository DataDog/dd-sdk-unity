// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Datadog.Unity.Editor
{
    public class DatadogConfigurationWindow : SettingsProvider
    {
        private bool _showAdvancedOptions;
        private DatadogConfigurationOptions _options;

        public DatadogConfigurationWindow(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, keywords)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateCustomSettingsProvider()
        {
            var provider = new DatadogConfigurationWindow("Project/Datadog", SettingsScope.Project, new string[] { "Datadog" });
            return provider;
        }

        /// <inheritdoc/>
        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _options = DatadogConfigurationOptionsExtensions.GetOrCreate();
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.Space();
            GUILayout.Label("SDK Options", EditorStyles.boldLabel);

            _options.Enabled = EditorGUILayout.ToggleLeft(
                new GUIContent("Enable Datadog", DatadogHelpStrings.EnabledTooltip),
                _options.Enabled);
            _options.OutputSymbols = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "Output Symbol Files",
                    DatadogHelpStrings.OutputSymbolsTooltip),
                _options.OutputSymbols);

            _options.ClientToken = EditorGUILayout.TextField(new GUIContent("Client Token", DatadogHelpStrings.ClientTokenTooltip), _options.ClientToken);
            _options.Env = EditorGUILayout.TextField(new GUIContent("Env", DatadogHelpStrings.EnvTooltip), _options.Env);
            _options.ServiceName = EditorGUILayout.TextField(new GUIContent("Service Name", DatadogHelpStrings.ServiceNameTooltip), _options.ServiceName);
            _options.Site = (DatadogSite)EditorGUILayout.EnumPopup(new GUIContent("Datadog Site", DatadogHelpStrings.SiteTooltip), _options.Site);
            _options.BatchSize = (BatchSize)EditorGUILayout.EnumPopup(new GUIContent("Batch Size", DatadogHelpStrings.BatchSizeTooltip), _options.BatchSize);
            _options.UploadFrequency = (UploadFrequency)EditorGUILayout.EnumPopup(
                new GUIContent("Upload Frequency", DatadogHelpStrings.UploadFrequencyTooltip), _options.UploadFrequency);
            _options.BatchProcessingLevel = (BatchProcessingLevel)EditorGUILayout.EnumPopup(
                new GUIContent("Batch Processing Level", DatadogHelpStrings.BatchProcessingLevelTooltip), _options.BatchProcessingLevel);
            _options.CrashReportingEnabled = EditorGUILayout.ToggleLeft(
                new GUIContent("Enable Crash Reporting", DatadogHelpStrings.CrashReportingEnabledTooltip),
                _options.CrashReportingEnabled);

            EditorGUILayout.Space();
            GUILayout.Label("Logging", EditorStyles.boldLabel);
            _options.ForwardUnityLogs = EditorGUILayout.ToggleLeft(
                new GUIContent("Forward Unity Logs", DatadogHelpStrings.ForwardUnityLogsTooltip),
                _options.ForwardUnityLogs);
            _options.RemoteLogThreshold = (LogType)EditorGUILayout.EnumPopup(
                new GUIContent("Remote Log Threshold", DatadogHelpStrings.RemoteLogThresholdTooltip), _options.RemoteLogThreshold);

            EditorGUILayout.Space();
            GUILayout.Label("RUM Options", EditorStyles.boldLabel);
            _options.RumEnabled = EditorGUILayout.ToggleLeft(
                new GUIContent("Enable RUM", DatadogHelpStrings.EnableRumTooltip),
                _options.RumEnabled);
            EditorGUI.BeginDisabledGroup(!_options.RumEnabled);
            _options.RumApplicationId = EditorGUILayout.TextField(
                new GUIContent("RUM Application Id",DatadogHelpStrings.RUMApplicationIdTooltip), _options.RumApplicationId);
            _options.AutomaticSceneTracking = EditorGUILayout.ToggleLeft(
                new GUIContent("Enable Automatic Scene Tracking", DatadogHelpStrings.EnableSceneTrackingTooltip),
                _options.AutomaticSceneTracking);
            _options.SessionSampleRate = EditorGUILayout.FloatField(
                new GUIContent("Session Sample Rate", DatadogHelpStrings.SessionSampleRateTooltip),
                _options.SessionSampleRate);
            _options.SessionSampleRate = Math.Clamp(_options.SessionSampleRate, 0.0f, 100.0f);
            _options.TraceSampleRate = EditorGUILayout.FloatField(
                new GUIContent("Trace Sample Rate", DatadogHelpStrings.TraceSampleRateTooltip),
                _options.TraceSampleRate);
            _options.TraceContextInjection = (TraceContextInjection)EditorGUILayout.EnumPopup(
                new GUIContent("Trace Context Injection", DatadogHelpStrings.TraceContextInjectionTooltip),
                _options.TraceContextInjection);
            _options.TraceSampleRate = Math.Clamp(_options.TraceSampleRate, 0.0f, 100.0f);
            _options.TrackNonFatalAnrs = (NonFatalAnrDetectionOption)EditorGUILayout.EnumPopup(
                new GUIContent("Track Non-Fatal ANRs", DatadogHelpStrings.TrackNonFatalAnrsTooltip),
                _options.TrackNonFatalAnrs);
            EditorGUILayout.BeginHorizontal();
            _options.TrackNonFatalAppHangs = EditorGUILayout.ToggleLeft(
                new GUIContent("Track Non-Fatal App Hangs", DatadogHelpStrings.TrackNonFatalAppHangsTooltip),
                _options.TrackNonFatalAppHangs);
            EditorGUI.BeginDisabledGroup(!_options.TrackNonFatalAppHangs);
            _options.NonFatalAppHangThreshold = EditorGUILayout.FloatField(
                new GUIContent("Threshold", DatadogHelpStrings.NonFatalAppHangThresholdTooltip),
                _options.NonFatalAppHangThreshold);
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12.0f);

            GUILayout.Label(new GUIContent("First Party Hosts", DatadogHelpStrings.FirstPartyHostsTooltip), EditorStyles.boldLabel);
            int toRemove = -1;
            for (int i = 0; i < _options.FirstPartyHosts.Count; ++i)
            {
                EditorGUILayout.BeginHorizontal();
                var hostOption = _options.FirstPartyHosts[i];
                hostOption.Host = EditorGUILayout.TextField(hostOption.Host);
                hostOption.TracingHeaderType = (TracingHeaderType)EditorGUILayout.EnumFlagsField(hostOption.TracingHeaderType);
                if (GUILayout.Button("X", GUILayout.ExpandWidth(false)))
                {
                    toRemove = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (toRemove >= 0)
            {
                _options.FirstPartyHosts.RemoveAt(toRemove);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Host", GUILayout.ExpandWidth(false)))
            {
                _options.FirstPartyHosts.Add(new FirstPartyHostOption());
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            _showAdvancedOptions = EditorGUILayout.BeginFoldoutHeaderGroup(_showAdvancedOptions, "Advanced RUM Options");
            if (_showAdvancedOptions)
            {
                _options.CustomEndpoint = EditorGUILayout.TextField(
                    new GUIContent("Custom Endpoint", DatadogHelpStrings.CustomEndpointTooltip), _options.CustomEndpoint);
                _options.SdkVerbosity = (CoreLoggerLevel)EditorGUILayout.EnumPopup(
                    new GUIContent("SDK Verbosity", DatadogHelpStrings.SdkVerbosityTooltip), _options.SdkVerbosity);
                _options.TelemetrySampleRate = EditorGUILayout.FloatField(
                    new GUIContent("Telemetry Sample Rate", DatadogHelpStrings.TelemetrySampleRateTooltip), _options.TelemetrySampleRate);
                _options.TelemetrySampleRate = Math.Clamp(_options.TelemetrySampleRate, 0.0f, 100.0f);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUI.EndDisabledGroup();
        }

        public override void OnDeactivate()
        {
            if (_options != null)
            {
                EditorUtility.SetDirty(_options);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
