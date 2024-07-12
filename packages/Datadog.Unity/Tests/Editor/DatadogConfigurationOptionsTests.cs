// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Datadog.Unity.Editor.Tests
{
    public class DatadogConfigurationOptionsTests
    {
        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            _assetPath = Path.Combine("Assets", Guid.NewGuid().ToString(), "DatadogSettings.asset");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Path.GetDirectoryName(_assetPath));
        }

        [Test]
        public void GetOrCreateCreatesAssetCreatesAsset()
        {
            Assert.IsFalse(File.Exists(_assetPath));

            DatadogConfigurationOptionsExtensions.GetOrCreate(_assetPath);

            Assert.IsTrue(File.Exists(_assetPath));
        }

        [Test]
        public void GetOrCreateCreatesAssetCreatesOptionsWithProperDefaults()
        {
            Assert.IsFalse(File.Exists(_assetPath));

            var options = DatadogConfigurationOptionsExtensions.GetOrCreate(_assetPath);

            // Base Config
            Assert.IsTrue(options.Enabled);
            Assert.AreEqual(options.SdkVerbosity, CoreLoggerLevel.Warn);
            Assert.IsFalse(options.OutputSymbols);
            Assert.IsEmpty(options.ClientToken);
            Assert.IsEmpty(options.Env);
            Assert.AreEqual(DatadogSite.Us1, options.Site);
            Assert.IsEmpty(options.CustomEndpoint);
            Assert.AreEqual(BatchSize.Medium, options.BatchSize);
            Assert.AreEqual(UploadFrequency.Average, options.UploadFrequency);
            Assert.AreEqual(BatchProcessingLevel.Medium, options.BatchProcessingLevel);
            Assert.IsTrue(options.CrashReportingEnabled);

            // Logging
            Assert.IsFalse(options.ForwardUnityLogs);
            Assert.AreEqual(LogType.Log, options.RemoteLogThreshold);

            // RUM
            Assert.IsFalse(options.RumEnabled);
            Assert.IsEmpty(options.RumApplicationId);
            Assert.IsTrue(options.AutomaticSceneTracking);
            Assert.AreEqual(VitalsUpdateFrequency.Average, options.VitalsUpdateFrequency);
            Assert.AreEqual(100.0f, options.SessionSampleRate);
            Assert.AreEqual(20.0f, options.TraceSampleRate);
            Assert.AreEqual(TraceContextInjection.All, options.TraceContextInjection);
            Assert.AreEqual(20.0f, options.TelemetrySampleRate);
        }
    }
}
