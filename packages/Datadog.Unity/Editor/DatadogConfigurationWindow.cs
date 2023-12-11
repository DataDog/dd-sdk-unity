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
                new GUIContent("Enable Datadog", "Whether the Datadog Plugin should be enabled or not."),
                _options.Enabled);
            _options.OutputSymbols = EditorGUILayout.ToggleLeft(
                new GUIContent(
                    "Output Symbol Files",
                    "Whether the Datadog Plugin should output symbol files for crash reporting."),
                _options.OutputSymbols);

            _options.ClientToken = EditorGUILayout.TextField("Client Token", _options.ClientToken);
            _options.Site = (DatadogSite)EditorGUILayout.EnumPopup("Datadog Site", _options.Site);
            _options.CustomEndpoint = EditorGUILayout.TextField("Custom Endpoint", _options.CustomEndpoint);
            _options.BatchSize = (BatchSize)EditorGUILayout.EnumPopup("Batch Size", _options.BatchSize);
            _options.UploadFrequency = (UploadFrequency)EditorGUILayout.EnumPopup("Upload Frequency", _options.UploadFrequency);

            EditorGUILayout.Space();
            GUILayout.Label("Logging", EditorStyles.boldLabel);
            _options.ForwardUnityLogs = EditorGUILayout.ToggleLeft(
                new GUIContent("Forward Unity Logs", "Whether calls to Debug.Log functions should be forwarded to Datadog."),
                _options.ForwardUnityLogs);
            _options.DefaultLoggingLevel = (LogType)EditorGUILayout.EnumPopup("Default Logging Level", _options.DefaultLoggingLevel);

            EditorGUILayout.Space();
            GUILayout.Label("RUM Options", EditorStyles.boldLabel);
            _options.RumEnabled = EditorGUILayout.ToggleLeft(
                new GUIContent("Enable RUM", "Whether to enable Real User Monitoring (RUM)"),
                _options.RumEnabled);
            EditorGUI.BeginDisabledGroup(!_options.RumEnabled);
            _options.AutomaticSceneTracking = EditorGUILayout.ToggleLeft(
                new GUIContent("Enable Automatic Scene Tracking", "Automatically start Datadog Views when Unity Scenes change"),
                _options.AutomaticSceneTracking);
            _options.RumApplicationId = EditorGUILayout.TextField("RUM Application Id", _options.RumApplicationId);
            _options.TelemetrySampleRate =
                EditorGUILayout.FloatField("Telemetry Sample Rate", _options.TelemetrySampleRate);
            _options.TelemetrySampleRate = Math.Clamp(_options.TelemetrySampleRate, 0.0f, 100.0f);

            EditorGUILayout.Space();
            _options.TraceSampleRate =
                EditorGUILayout.FloatField("Trace Sample Rate", _options.TraceSampleRate);
            _options.TraceSampleRate = Math.Clamp(_options.TraceSampleRate, 0.0f, 100.0f);

            GUILayout.Label("First Party Hosts", EditorStyles.boldLabel);
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
