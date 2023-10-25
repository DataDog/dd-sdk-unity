// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Datadog.Unity.Editor.iOS;
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
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, new DatadogConfigurationOptions());

            File.Exists(_initializationFilePath);
        }

        [Test]
        public void GenerateOptionsFileWritesAutoGenerationWarning()
        {
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, new DatadogConfigurationOptions());

            string fileContents = File.ReadAllText(_initializationFilePath);
            Assert.IsTrue(fileContents.Contains("THIS FILE IS AUTO GENERATED"));
        }

        [TestCase(BatchSize.Small, ".small")]
        [TestCase(BatchSize.Medium, ".medium")]
        [TestCase(BatchSize.Large, ".large")]
        public void GenerationOptionsFileWritesBatchSize(BatchSize batchSize, string expectedBatchSizeString)
        {
            var options = new DatadogConfigurationOptions()
            {
                BatchSize = batchSize,
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options);

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
                UploadFrequency = uploadFrequency,
            };
            PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options);

            var lines = File.ReadAllLines(_initializationFilePath);
            var uploadFrequencyLines = lines.Where(l => l.Contains("uploadFrequency:")).ToArray();
            Assert.AreEqual(1, uploadFrequencyLines.Length);
            Assert.AreEqual($"uploadFrequency: {expectedUploadFrequency}", uploadFrequencyLines.First().Trim());
        }

         [TestCase(0.0f)]
         [TestCase(12.0f)]
         [TestCase(100.0f)]
         public void GenerateOptionsFileWritesTelemetrySampleRate(float sampleRate)
         {
             var options = new DatadogConfigurationOptions()
             {
                 TelemetrySampleRate = sampleRate,
             };
             PostBuildProcess.GenerateInitializationFile(_initializationFilePath, options);

             var lines = File.ReadAllLines(_initializationFilePath);
             var sampleTelemetryLines = lines.Where(l => l.Contains("telemetrySampleRate ="));
             var telemetryLines = sampleTelemetryLines as string[] ?? sampleTelemetryLines.ToArray();
             Assert.AreEqual(1, telemetryLines.Length);
             Assert.AreEqual($"rumConfig.telemetrySampleRate = {sampleRate}", telemetryLines.First().Trim());
         }

        [Test]
        public void AddInitializationToMainAddsDatadogBlocks()
        {
            var options = new DatadogConfigurationOptions();
            PostBuildProcess.AddInitializationToMain(_mainFilePath, options);

            string fileContents = File.ReadAllText(_mainFilePath);

            var includeBlock = @"// > Datadog Generated Block
#import ""DatadogOptions.h""
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
            PostBuildProcess.AddInitializationToMain(_mainFilePath, options);

            var fileContents = File.ReadAllLines(_mainFilePath);
            var cleanContents = PostBuildProcess.RemoveDatadogBlocks(new List<string>(fileContents));

            Assert.IsNull(cleanContents.FirstOrDefault(l => l.Contains("Datadog")));
        }
    }
}
