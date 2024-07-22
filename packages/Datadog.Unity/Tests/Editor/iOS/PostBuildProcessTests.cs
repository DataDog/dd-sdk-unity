// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.
#if UNITY_EDITOR_OSX

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

// Disable "Scriptable Objects should not be instantiated directly"
#pragma warning disable UNT0011

namespace Datadog.Unity.Editor.iOS
{
    public class PostBuildProcessTests
    {
        private static readonly string _cleanMainfile = "main.txt";

        private string _tempDirectory;
        private string _initializationFilePath;
        private string _mainFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine("tmp", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _initializationFilePath = Path.Combine(_tempDirectory, "DatadogInitialization.swift");
            _mainFilePath = Path.Combine(_tempDirectory, _cleanMainfile);
            File.Copy(_cleanMainfile, _mainFilePath);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete("tmp", true);
        }

        [Test]
        public void GenerateOptionsFileCreatesFile()
        {
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, new DatadogConfigurationOptions(),
                null);

            File.Exists(_initializationFilePath);
        }

        [Test]
        public void GenerateOptionsFileWritesAutoGenerationWarning()
        {
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, new DatadogConfigurationOptions(),
                null);

            string fileContents = File.ReadAllText(_initializationFilePath);
            Assert.IsTrue(fileContents.Contains("THIS FILE IS AUTO GENERATED"));
        }

        [Test]
        public void GenerateOptionsFileWritesCustomSource()
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                CrashReportingEnabled = false
            };

            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var sourceLines = lines.Where(l => l.Contains("_dd.source")).ToArray();
            Assert.AreEqual(1, sourceLines.Length);
            Assert.IsTrue(sourceLines.First().Trim().StartsWith("\"_dd.source\": \"unity\""));
        }

        [TestCase(CoreLoggerLevel.Debug, ".debug")]
        [TestCase(CoreLoggerLevel.Warn, ".warn")]
        [TestCase(CoreLoggerLevel.Error, ".error")]
        [TestCase(CoreLoggerLevel.Critical, ".critical")]
        public void GenerationOptionsFileWritesSdkVerbosity(CoreLoggerLevel loggerLevel, string expectedCoreLoggingLevel)
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                SdkVerbosity = loggerLevel,
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var verbosityLevelLines = lines.Where(l => l.Contains("Datadog.verbosityLevel")).ToArray();
            Assert.AreEqual(1, verbosityLevelLines.Length);
            Assert.AreEqual($"Datadog.verbosityLevel = {expectedCoreLoggingLevel}", verbosityLevelLines.First().Trim());
        }

        [TestCase(BatchSize.Small, ".small")]
        [TestCase(BatchSize.Medium, ".medium")]
        [TestCase(BatchSize.Large, ".large")]
        public void GenerationOptionsFileWritesBatchSize(BatchSize batchSize, string expectedBatchSizeString)
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                BatchSize = batchSize,
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var batchSizeLines = lines.Where(l => l.Contains("batchSize:")).ToArray();
            Assert.AreEqual(1, batchSizeLines.Length);
            Assert.AreEqual($"batchSize: {expectedBatchSizeString},", batchSizeLines.First().Trim());
        }

        [TestCase(UploadFrequency.Rare, ".rare")]
        [TestCase(UploadFrequency.Average, ".average")]
        [TestCase(UploadFrequency.Frequent, ".frequent")]
        public void GenerationOptionsFileWritesUploadFrequency(UploadFrequency uploadFrequency,
            string expectedUploadFrequency)
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                UploadFrequency = uploadFrequency,
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var uploadFrequencyLines = lines.Where(l => l.Contains("uploadFrequency:")).ToArray();
            Assert.AreEqual(1, uploadFrequencyLines.Length);
            Assert.IsTrue(uploadFrequencyLines.First().Trim().StartsWith($"uploadFrequency: {expectedUploadFrequency}"));
        }

        [TestCase(BatchProcessingLevel.Low, ".low")]
        [TestCase(BatchProcessingLevel.Medium, ".medium")]
        [TestCase(BatchProcessingLevel.High, ".high")]
        public void GenerationOptionsFileWritesBatchProcessingLevel(
            BatchProcessingLevel processingLevel, string expectedProcessingLevel)
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                BatchProcessingLevel = processingLevel,
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var processingLevelLines = lines.Where(l => l.Contains("batchProcessingLevel:")).ToArray();
            Assert.AreEqual(1, processingLevelLines.Length);
            Assert.IsTrue(processingLevelLines.First().Trim().StartsWith($"batchProcessingLevel: {expectedProcessingLevel}"));
        }

        [TestCase(DatadogSite.Us1, ".us1")]
        [TestCase(DatadogSite.Us3, ".us3")]
        [TestCase(DatadogSite.Us5, ".us5")]
        [TestCase(DatadogSite.Ap1, ".ap1")]
        [TestCase(DatadogSite.Eu1, ".eu1")]
        [TestCase(DatadogSite.Us1Fed, ".us1_fed")]
        public void GenerationOptionsFileWritesSite(DatadogSite site, string expectedSite)
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                Site = site,
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var siteLines = lines.Where(l => l.Contains("site:")).ToArray();
            Assert.AreEqual(1, siteLines.Length);
            Assert.IsTrue(siteLines.First().Trim().StartsWith($"site: {expectedSite},"));
        }

        [Test]
        public void GenerateOptionsFileDoesNotWriteCrashReportingIfDisabled()
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                CrashReportingEnabled = false
            };

            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var crashReportingLines = lines.Where(l => l.Contains("CrashReporting.enable()")).ToArray();
            Assert.IsEmpty(crashReportingLines);
        }

        [Test]
        public void GenerateOptionsFileWritesCrashReportingIfEnabled()
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                CrashReportingEnabled = true
            };

            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var crashReportingLines = lines.Where(l => l.Contains("CrashReporting.enable()")).ToArray();
            Assert.AreEqual(1, crashReportingLines.Length);
        }

        [TestCase(0.0f)]
        [TestCase(84.0f)]
        [TestCase(100.0f)]
        public void GenerateOptionsFileWritesSessionSampleRate(float sampleRate)
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                RumEnabled = true,
                SessionSampleRate = sampleRate,
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var sampleSessionRateLines = lines.Where(l => l.Contains("sessionSampleRate ="));
            var sessionRateLines = sampleSessionRateLines as string[] ?? sampleSessionRateLines.ToArray();
            Assert.AreEqual(1, sessionRateLines.Length);
            Assert.AreEqual($"rumConfig.sessionSampleRate = {sampleRate}", sessionRateLines.First().Trim());
        }

        [Test]
        public void GenerateOptionsFileRemovesVitalsUpdateFrequencyWhenNone()
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                RumEnabled = true,
                VitalsUpdateFrequency = VitalsUpdateFrequency.None
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var uploadFrequencyLines = lines.Where(l => l.Contains("vitalsUpdateFrequency =")).ToArray();
            Assert.AreEqual(0, uploadFrequencyLines.Length);
        }

        [TestCase(VitalsUpdateFrequency.Rare, ".rare")]
        [TestCase(VitalsUpdateFrequency.Average, ".average")]
        [TestCase(VitalsUpdateFrequency.Frequent, ".frequent")]
        public void GenerationOptionsFileWritesVitalUpdateFrequency(VitalsUpdateFrequency updateFrequency,
            string expectedUpdateFrequency)
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                RumEnabled = true,
                VitalsUpdateFrequency = updateFrequency
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var uploadFrequencyLines = lines.Where(l => l.Contains("vitalsUpdateFrequency =")).ToArray();
            Assert.AreEqual(1, uploadFrequencyLines.Length);
            Assert.AreEqual($"rumConfig.vitalsUpdateFrequency = {expectedUpdateFrequency}", uploadFrequencyLines.First().Trim());
        }

        [TestCase(0.0f)]
        [TestCase(12.0f)]
        [TestCase(100.0f)]
        public void GenerateOptionsFileWritesTelemetrySampleRate(float sampleRate)
        {
            var options = new DatadogConfigurationOptions()
            {
                Enabled = true,
                RumEnabled = true,
                TelemetrySampleRate = sampleRate,
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var sampleTelemetryLines = lines.Where(l => l.Contains("telemetrySampleRate ="));
            var telemetryLines = sampleTelemetryLines as string[] ?? sampleTelemetryLines.ToArray();
            Assert.AreEqual(1, telemetryLines.Length);
            Assert.AreEqual($"rumConfig.telemetrySampleRate = {sampleRate}", telemetryLines.First().Trim());
        }

        [Test]
        public void MissingBuildIdDoesNotWriteBuildId()
        {
            var options = new DatadogConfigurationOptions();
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var buildIdLines = lines.Where(l => l.Contains("\"_dd.build_id:\""));
            Assert.AreEqual(0, buildIdLines.Count());
        }

        [Test]
        public void GeneratedBuildIdWritesBuildId()
        {
            var uuid = Guid.NewGuid().ToString();

            var options = new DatadogConfigurationOptions()
            {
                Enabled = true
            };

            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, uuid);

			var lines = File.ReadAllLines(_initializationFilePath);
            var buildIdLines = lines.Where(l => l.Contains("\"_dd.build_id\":"));
            Assert.AreEqual(1, buildIdLines.Count());
            // Use "StartsWith" to avoid issues with the line ending in a comma
            Assert.IsTrue(buildIdLines.First().Trim().StartsWith($"\"_dd.build_id\": \"{uuid}\""));
		}

        [Test]
        public void ConfigWritesSdkVersion()
        {
            var sdkVersion = typeof(DatadogSdk).Assembly.GetName().Version;
            Assert.IsNotNull(sdkVersion);
            var expectedVersion = $"{sdkVersion.Major}.{sdkVersion.Minor}.{sdkVersion.Build}";

            var options = new DatadogConfigurationOptions()
            {
                Enabled = true
            };

            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var sdkVersionLines = lines.Where(l => l.Contains("\"_dd.sdk_version\":")).ToArray();
            Assert.AreEqual(1, sdkVersionLines.Length);
            Assert.IsTrue(sdkVersionLines.First().Trim().StartsWith($"\"_dd.sdk_version\": \"{expectedVersion}\""));
        }

		[Test]
        public void GenerateOptionsFileWritesDefaultEnv()
        {
            var options = new DatadogConfigurationOptions();
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var envLines = lines.Where(l => l.Contains("env: ")).ToArray();
            Assert.AreEqual(1, envLines.Length);
            Assert.AreEqual($"env: \"prod\",", envLines.First().Trim());
        }

        [Test]
        public void GenerateOptionsFileDoesNotWriteEmptyService()
        {
            var options = new DatadogConfigurationOptions();
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var serviceLines = lines.Where(l => l.Contains("service: ")).ToArray();
            Assert.IsEmpty(serviceLines);
        }

        [Test]
        public void GenerateOptionsFileAddsService()
        {
            var options = new DatadogConfigurationOptions();
            options.ServiceName = "service-from-options";
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var serviceLines = lines.Where(l => l.Contains("service: ")).ToArray();
            Assert.AreEqual(1, serviceLines.Length);
            Assert.AreEqual($"service: \"service-from-options\",", serviceLines.First().Trim());
        }

        [Test]
        public void GenerateOptionsFileWritesEnvFromOptions()
        {
            var options = new DatadogConfigurationOptions()
            {
                Env = "env-from-options",
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options, null);

            var lines = File.ReadAllLines(_initializationFilePath);
            var envLines = lines.Where(l => l.Contains("env: ")).ToArray();
            Assert.AreEqual(1, envLines.Length);
            Assert.AreEqual($"env: \"env-from-options\",", envLines.First().Trim());
        }

        [Test]
        public void AddInitializationToMainAddsDatadogBlocks()
        {
            var importString = "#import";

            var options = new DatadogConfigurationOptions()
            {
                Enabled = true
            };

            PostBuildProcess.AddInitializationToAppController(_mainFilePath, options);

            string fileContents = File.ReadAllText(_mainFilePath);

            var includeBlock = @$"// > Datadog Generated Block
{importString} ""DatadogOptions.h""
// < End Datadog Generated Block";

            var initializationBlock = @"        // > Datadog Generated Block
        initializeDatadog();
        // < End Datadog Generated Block";

            Assert.IsTrue(fileContents.Contains(includeBlock));
            Assert.IsTrue(fileContents.Contains(initializationBlock));
        }

        [Test]
        public void RemoveDatadogBlocksRemovesDatadogBlocks()
        {
            var options = new DatadogConfigurationOptions();
            PostBuildProcess.AddInitializationToAppController(_mainFilePath, options);

            var fileContents = File.ReadAllLines(_mainFilePath);
            var cleanContents = PostBuildProcess.RemoveDatadogBlocks(new List<string>(fileContents));

            Assert.IsNull(cleanContents.FirstOrDefault(l => l.Contains("Datadog")));
        }
    }
}

#endif
