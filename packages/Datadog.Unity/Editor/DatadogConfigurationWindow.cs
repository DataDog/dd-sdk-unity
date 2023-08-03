// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

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
            _options.RumApplicationId = EditorGUILayout.TextField("RUM Application Id", _options.RumApplicationId);
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
