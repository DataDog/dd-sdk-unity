// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Datadog.Unity.Tests.Integration
{
    public sealed class IntegrationTestSettings
    {
        private readonly string _assetPath;
        private readonly string _backupPath;

        public IntegrationTestSettings(string assetPath, string backupPath)
        {
            _assetPath = assetPath;
            _backupPath = backupPath;
        }

        public void Apply(string endpoint)
        {
            var serverUri = new Uri(endpoint, UriKind.Absolute);
            Restore();

            var settings = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(_assetPath);
            if (settings == null)
            {
                throw new InvalidOperationException("Create the project's Datadog Settings asset before running integration tests.");
            }

            // Preserve unsaved Inspector edits as well as the asset's exact disk format.
            AssetDatabase.SaveAssetIfDirty(settings);
            Directory.CreateDirectory(Path.GetDirectoryName(_backupPath));
            File.Copy(_assetPath, _backupPath);
            try
            {
                settings.Enabled = true;
                settings.SdkVerbosity = CoreLoggerLevel.Warn;
                settings.ClientToken = "fake-client-token";
                settings.Env = "integration-test";
                settings.ServiceName = "datadog-sample";
                settings.CustomEndpoint = endpoint;
                settings.BatchSize = BatchSize.Medium;
                settings.UploadFrequency = UploadFrequency.Average;
                settings.BatchProcessingLevel = BatchProcessingLevel.Medium;
                settings.CrashReportingEnabled = true;
                settings.ForwardUnityLogs = true;
                settings.RemoteLogThreshold = LogType.Log;
                settings.RumEnabled = true;
                settings.RumApplicationId = "fake-rum-application-id";
                settings.AutomaticSceneTracking = true;
                settings.SessionSampleRate = 100;
                settings.TraceSampleRate = 100;
                settings.TelemetrySampleRate = 100;
                settings.FirstPartyHosts = new List<FirstPartyHostOption>
                {
                    new FirstPartyHostOption(serverUri.Authority,
                        TracingHeaderType.Datadog | TracingHeaderType.TraceContext),
                };
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
                // Finish importing the test settings before cleanup can restore the original file.
                AssetDatabase.ImportAsset(_assetPath,
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            catch
            {
                Restore();
                throw;
            }
        }

        public void Restore()
        {
            if (!File.Exists(_backupPath))
            {
                return;
            }

            var settings = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(_assetPath);
            if (settings != null)
            {
                EditorUtility.ClearDirty(settings);
            }

            File.Copy(_backupPath, _assetPath, overwrite: true);
            AssetDatabase.ImportAsset(_assetPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            File.Delete(_backupPath);
        }
    }
}
#endif
