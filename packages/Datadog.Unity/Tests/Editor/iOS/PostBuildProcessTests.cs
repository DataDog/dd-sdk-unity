// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.IO;
using NUnit.Framework;
using Datadog.Unity.Editor.iOS;
using System.Linq;
using System.Collections.Generic;

namespace Datadog.Unity.Editor.iOS
{
    public class PostBuildProcessTests
    {
        private static string _cleanMainfile = "main.txt";

        private string _tempDirectory;
        private string _optionsFilePath;
        private string _mainFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine("tmp", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            _optionsFilePath = Path.Combine(_tempDirectory, "options.m");
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
            DatadogBuildProcess.GenerateOptionsFile(_optionsFilePath, new DatadogConfigurationOptions());

            File.Exists(_optionsFilePath);
        }

        [Test]
        public void GenerateOptionsFileWritesAutoGenerationWarning()
        {
            DatadogBuildProcess.GenerateOptionsFile(_optionsFilePath, new DatadogConfigurationOptions());

            string fileContents = File.ReadAllText(_optionsFilePath);
            Assert.IsTrue(fileContents.Contains("THIS FILE IS AUTO GENERATED"));
        }

        [TestCase(BatchSize.Small, "DDBatchSizeSmall")]
        [TestCase(BatchSize.Medium, "DDBatchSizeMedium")]
        [TestCase(BatchSize.Large, "DDBatchSizeLarge")]
        public void GenerationOptionsFileWritesBatchSize(BatchSize batchSize, string expectedBatchSizeString)
        {
            var options = new DatadogConfigurationOptions()
            {
                BatchSize = batchSize
            };
            DatadogBuildProcess.GenerateOptionsFile(_optionsFilePath, options);

            var lines = File.ReadAllLines(_optionsFilePath);
            var batchSizeLines = lines.Where(l => l.Contains("setWithBatchSize"));
            Assert.AreEqual(1, batchSizeLines.Count());
            Assert.AreEqual($"[builder setWithBatchSize:{expectedBatchSizeString}];", batchSizeLines.First().Trim());
        }

        [TestCase(UploadFrequency.Rare, "DDUploadFrequencyRare")]
        [TestCase(UploadFrequency.Average, "DDUploadFrequencyAverage")]
        [TestCase(UploadFrequency.Frequent, "DDUploadFrequencyFrequent")]
        public void GenerationOptionsFileWritesUploadFrequency(UploadFrequency uploadFrequency, string expectedUploadFrequency)
        {
            var options = new DatadogConfigurationOptions()
            {
                UploadFrequency = uploadFrequency
            };
            DatadogBuildProcess.GenerateOptionsFile(_optionsFilePath, options);

            var lines = File.ReadAllLines(_optionsFilePath);
            var uploadFrequencyLines = lines.Where(l => l.Contains("setWithUploadFrequency"));
            Assert.AreEqual(1, uploadFrequencyLines.Count());
            Assert.AreEqual($"[builder setWithUploadFrequency:{expectedUploadFrequency}];", uploadFrequencyLines.First().Trim());
        }

        [Test]
        public void AddInitializationToMainAddsDatadogBlocks()
        {
            DatadogBuildProcess.AddInitializationToMain(_mainFilePath);

            string fileContents = File.ReadAllText(_mainFilePath);

            var includeBlock = @"// > Datadog Generated Block
#import <Datadog/Datadog-Swift.h>
#import <DatadogObjc/DatadogObjc-Swift.h>
#import ""DatadogOptions.h""
// < End Datadog Generated Block";

            var initializationBlock = @"        // > Datadog Generated Block
        [DDDatadog initializeWithAppContext:[DDAppContext new]
                            trackingConsent:[DDTrackingConsent pending]
                              configuration:buildDatadogConfiguration()];
        // < End Datadog Generated Block";

            Assert.IsTrue(fileContents.Contains(includeBlock));
            Assert.IsTrue(fileContents.Contains(initializationBlock));
        }

        [Test]
        public void RemoveDatadogBlocksRemovesDatadogBlocks()
        {
            DatadogBuildProcess.AddInitializationToMain(_mainFilePath);

            var fileContents = File.ReadAllLines(_mainFilePath);
            var cleanContents = DatadogBuildProcess.RemoveDatadogBlocks(new List<string>(fileContents));

            Assert.IsNull(cleanContents.FirstOrDefault(l => l.Contains("Datadog")));
        }
    }
}
