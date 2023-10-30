// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Datadog.Unity.Editor
{
    public static class DatadogConfigurationOptionsExtensions
    {
        public static DatadogConfigurationOptions GetOrCreate(string settingsPath = null)
        {
            settingsPath ??= DatadogConfigurationOptions.DefaultDatadogSettingsPath;
            var options = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(settingsPath);
            if (options == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));

                options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
                options.ClientToken = string.Empty;
                options.Enabled = true;
                options.Site = DatadogSite.Us1;
                options.CustomEndpoint = string.Empty;
                options.DefaultLoggingLevel = LogType.Log;
                options.BatchSize = BatchSize.Medium;
                options.UploadFrequency = UploadFrequency.Average;
                options.ForwardUnityLogs = false;
                options.RumEnabled = false;
                options.AutomaticSceneTracking = true;
                options.RumApplicationId = string.Empty;
                options.TelemetrySampleRate = 20.0f;

                AssetDatabase.CreateAsset(options, settingsPath);
                AssetDatabase.SaveAssets();
            }

            return options;
        }
    }
}
