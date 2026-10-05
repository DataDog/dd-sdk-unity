// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.IO;
using Datadog.Unity.Tests.Integration;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Datadog.Unity.Editor.Tests
{
    public class IntegrationTestSettingsTests
    {
        private string _assetDirectory;
        private string _assetPath;
        private string _backupDirectory;
        private string _backupPath;
        private IntegrationTestSettings _settings;

        [SetUp]
        public void SetUp()
        {
            var name = "IntegrationSettingsTests-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _assetDirectory = "Assets/" + name;
            _assetPath = _assetDirectory + "/DatadogSettings.asset";
            _backupDirectory = Path.GetFullPath("Library/" + name);
            _backupPath = Path.Combine(_backupDirectory, "DatadogSettings.original");
            _settings = new IntegrationTestSettings(_assetPath, _backupPath);

            var original = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            original.Env = "original-environment";
            original.ClientToken = "original-token";
            original.ServiceName = "original-service";
            original.CustomEndpoint = "https://original.example";
            original.FirstPartyHosts = new List<FirstPartyHostOption>
            {
                new FirstPartyHostOption("original.example", TracingHeaderType.B3),
            };
            original.Site = DatadogSite.Eu1;
            original.OutputSymbols = true;
            original.TraceSampleRate = 17;
            original.SessionSampleRate = 23;
            original.TelemetrySampleRate = 31;
            AssetDatabase.CreateAsset(original, _assetPath);
        }

        [TearDown]
        public void TearDown()
        {
            _settings.Restore();
            AssetDatabase.DeleteAsset(_assetDirectory);
            if (Directory.Exists(_backupDirectory))
            {
                Directory.Delete(_backupDirectory, recursive: true);
            }
        }

        [Test]
        public void ApplyPersistsIntegrationConfigurationThroughUnity()
        {
            _settings.Apply("http://192.0.2.10:5100");
            AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceUpdate);
            var configured = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(_assetPath);

            Assert.IsTrue(configured.Enabled);
            Assert.AreEqual(CoreLoggerLevel.Warn, configured.SdkVerbosity);
            Assert.AreEqual("fake-client-token", configured.ClientToken);
            Assert.AreEqual("integration-test", configured.Env);
            Assert.AreEqual("datadog-sample", configured.ServiceName);
            Assert.AreEqual("http://192.0.2.10:5100", configured.CustomEndpoint);
            Assert.AreEqual(BatchSize.Medium, configured.BatchSize);
            Assert.AreEqual(UploadFrequency.Average, configured.UploadFrequency);
            Assert.AreEqual(BatchProcessingLevel.Medium, configured.BatchProcessingLevel);
            Assert.IsTrue(configured.CrashReportingEnabled);
            Assert.IsTrue(configured.ForwardUnityLogs);
            Assert.AreEqual(LogType.Log, configured.RemoteLogThreshold);
            Assert.IsTrue(configured.RumEnabled);
            Assert.AreEqual("fake-rum-application-id", configured.RumApplicationId);
            Assert.IsTrue(configured.AutomaticSceneTracking);
            Assert.AreEqual(100, configured.SessionSampleRate);
            Assert.AreEqual(100, configured.TraceSampleRate);
            Assert.AreEqual(100, configured.TelemetrySampleRate);
            Assert.AreEqual(1, configured.FirstPartyHosts.Count);
            Assert.AreEqual("192.0.2.10:5100", configured.FirstPartyHosts[0].Host);
            Assert.AreEqual(TracingHeaderType.Datadog | TracingHeaderType.TraceContext,
                configured.FirstPartyHosts[0].TracingHeaderType);
            Assert.AreEqual(DatadogSite.Eu1, configured.Site);
            Assert.IsTrue(configured.OutputSymbols);
        }

        [Test]
        public void RestoreRecoversExactAssetAndMetadataAfterLosingInMemoryState()
        {
            var original = File.ReadAllBytes(_assetPath);
            var metadata = File.ReadAllBytes(_assetPath + ".meta");
            _settings.Apply("http://192.0.2.10:5100");

            new IntegrationTestSettings(_assetPath, _backupPath).Restore();

            CollectionAssert.AreEqual(original, File.ReadAllBytes(_assetPath));
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(_assetPath + ".meta"));
            var restored = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(_assetPath);
            Assert.AreEqual("original-environment", restored.Env);
            Assert.AreEqual("original.example", restored.FirstPartyHosts[0].Host);
            Assert.IsFalse(File.Exists(_backupPath));
        }

        [Test]
        public void RestorePreservesInspectorEditsMadeBeforeSetup()
        {
            var original = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(_assetPath);
            original.Env = "unsaved-inspector-edit";
            EditorUtility.SetDirty(original);

            _settings.Apply("http://192.0.2.10:5100");
            _settings.Restore();

            Assert.AreEqual("unsaved-inspector-edit",
                AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(_assetPath).Env);
        }

        [Test]
        public void RepeatedCleanupPreservesEditsMadeAfterRestoration()
        {
            _settings.Apply("http://192.0.2.10:5100");
            _settings.Restore();
            var restored = AssetDatabase.LoadAssetAtPath<DatadogConfigurationOptions>(_assetPath);
            restored.ServiceName = "edited-after-build";
            EditorUtility.SetDirty(restored);
            AssetDatabase.SaveAssetIfDirty(restored);
            var edited = File.ReadAllBytes(_assetPath);

            _settings.Restore();

            CollectionAssert.AreEqual(edited, File.ReadAllBytes(_assetPath));
        }

        [Test]
        public void RepeatedSetupRetainsOriginalSnapshot()
        {
            var original = File.ReadAllBytes(_assetPath);
            _settings.Apply("http://192.0.2.10:5100");
            new IntegrationTestSettings(_assetPath, _backupPath).Apply("http://192.0.2.11:5100");

            _settings.Restore();

            CollectionAssert.AreEqual(original, File.ReadAllBytes(_assetPath));
        }

        [Test]
        public void InvalidEndpointLeavesAssetUntouched()
        {
            var original = File.ReadAllBytes(_assetPath);

            Assert.Throws<UriFormatException>(() => _settings.Apply("not an absolute URL"));

            CollectionAssert.AreEqual(original, File.ReadAllBytes(_assetPath));
            Assert.IsFalse(File.Exists(_backupPath));
        }

        [Test]
        public void MissingAssetDoesNotCreateBackup()
        {
            var missing = new IntegrationTestSettings(_assetDirectory + "/Missing.asset", _backupPath);

            Assert.Throws<InvalidOperationException>(() => missing.Apply("http://192.0.2.10:5100"));

            Assert.IsFalse(File.Exists(_backupPath));
        }
    }
}
