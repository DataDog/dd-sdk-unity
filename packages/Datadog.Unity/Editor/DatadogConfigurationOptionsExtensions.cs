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
            settingsPath ??= DatadogConfigurationOptions._DefaultDatadogSettingsPath;
            var options = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(settingsPath);
            if (options == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));

                options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
                options.ClientToken = string.Empty;
                options.Enabled = true;
                options.Site = DatadogSite.us1;
                options.DefaultLoggingLevel = LogType.Log;

                AssetDatabase.CreateAsset(options, settingsPath);
                AssetDatabase.SaveAssets();
            }
            return options;
        }
    }
}