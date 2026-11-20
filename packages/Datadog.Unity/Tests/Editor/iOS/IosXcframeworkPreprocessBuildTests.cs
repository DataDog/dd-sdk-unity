// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Build;

namespace Datadog.Unity.Editor.iOS
{
    public class IosXcframeworkPreprocessBuildTests
    {
        private static readonly string[] RequiredFiles = new[]
        {
            "DatadogCore.xcframework",
            "DatadogInternal.xcframework",
            "DatadogLogs.xcframework",
            "DatadogRUM.xcframework",
            "DatadogCrashReporting.xcframework",
        };

        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine("tmp", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete("tmp", true);
        }

        private void CreatePluginDirectories(params string[] files)
        {
            foreach (var file in files)
            {
                Directory.CreateDirectory(Path.Combine(_tempDirectory, file));
            }
        }

        [Test]
        public void FindMissingFilesReturnsEmptyWhenAllFilesPresent()
        {
            CreatePluginDirectories(RequiredFiles);

            var missing = IosXcframeworkPreprocessBuild.FindMissingFiles(_tempDirectory, RequiredFiles);

            Assert.IsEmpty(missing);
        }

        [Test]
        public void FindMissingFilesReturnsSingleMissingFile()
        {
            CreatePluginDirectories(RequiredFiles.Take(4).ToArray());

            var missing = IosXcframeworkPreprocessBuild.FindMissingFiles(_tempDirectory, RequiredFiles);

            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual(RequiredFiles[4], missing[0]);
        }

        [Test]
        public void FindMissingFilesReturnsAllFilesWhenDirectoryDoesNotExist()
        {
            var nonExistentDirectory = Path.Combine(_tempDirectory, "does-not-exist");

            var missing = IosXcframeworkPreprocessBuild.FindMissingFiles(nonExistentDirectory, RequiredFiles);

            Assert.AreEqual(RequiredFiles.Length, missing.Count);
            foreach (var module in RequiredFiles)
            {
                Assert.Contains(module, missing);
            }
        }

        [Test]
        public void FindMissingFilesReportsFileMissingWhenPathIsAFileNotDirectory()
        {
            CreatePluginDirectories(RequiredFiles.Skip(1).ToArray());
            File.WriteAllText(Path.Combine(_tempDirectory, RequiredFiles[0]), "not a directory");

            var missing = IosXcframeworkPreprocessBuild.FindMissingFiles(_tempDirectory, RequiredFiles);

            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual(RequiredFiles[0], missing[0]);
        }

        [Test]
        public void ValidateThrowsBuildFailedExceptionWhenFilesMissing()
        {
            CreatePluginDirectories(RequiredFiles.Take(3).ToArray());
            var absoluteDir = Path.GetFullPath(_tempDirectory);

            var ex = Assert.Throws<BuildFailedException>(
                () => IosXcframeworkPreprocessBuild.Validate(absoluteDir, RequiredFiles));

            Assert.IsTrue(ex.Message.Contains(RequiredFiles[3]));
            Assert.IsTrue(ex.Message.Contains(RequiredFiles[4]));
            Assert.IsTrue(ex.Message.Contains(absoluteDir));
            Assert.IsTrue(ex.Message.Contains("dd-unity-manage-sdk-dependencies stage"));
        }

        [Test]
        public void ValidateDoesNotThrowWhenAllFilesPresent()
        {
            CreatePluginDirectories(RequiredFiles);
            var absoluteDir = Path.GetFullPath(_tempDirectory);

            Assert.DoesNotThrow(() => IosXcframeworkPreprocessBuild.Validate(absoluteDir, RequiredFiles));
        }

        [TestCase("12.0")]
        [TestCase("14.0")]
        [TestCase("14.9")]
        [TestCase("0.0")]
        [TestCase("")]
        [TestCase(null)]
        [TestCase("invalid")]
        public void ValidateDeploymentTargetRejectsUnsupportedOrInvalidVersion(string targetVersion)
        {
            var exception = Assert.Throws<BuildFailedException>(
                () => IosXcframeworkPreprocessBuild.ValidateDeploymentTarget(targetVersion));

            StringAssert.Contains("iOS 15.0 or later", exception.Message);
            StringAssert.Contains("Player Settings > Other Settings > Target minimum iOS Version", exception.Message);
            StringAssert.Contains($"currently '{targetVersion}'", exception.Message);
        }

        [TestCase("15")]
        [TestCase("15.0")]
        [TestCase("15.0.1")]
        [TestCase("15.1")]
        [TestCase("16.0")]
        [TestCase("26.0")]
        public void ValidateDeploymentTargetAcceptsSupportedVersion(string targetVersion)
        {
            Assert.DoesNotThrow(() => IosXcframeworkPreprocessBuild.ValidateDeploymentTarget(targetVersion));
        }

        [Test]
        public void IosDependencyVersionParseReadsSelectedFiles()
        {
            var json = @"{
  ""version"": ""3.11.1"",
  ""artifacts"": {
    ""ios"": {
      ""destination"": ""Plugins/iOS/SDK"",
      ""sha256"": ""9fe66c4b4c4e3ba68b253c701aff97447358b08b0c5b43af6d4854bf1563c13d"",
      ""files"": {
        ""DatadogCore.xcframework"": ""DatadogCore.xcframework"",
        ""DatadogInternal.xcframework"": ""DatadogInternal.xcframework"",
        ""DatadogLogs.xcframework"": ""DatadogLogs.xcframework"",
        ""DatadogRUM.xcframework"": ""DatadogRUM.xcframework"",
        ""DatadogCrashReporting.xcframework"": ""DatadogCrashReporting.xcframework""
      }
    }
  }
}";

            var data = IosDependencyVersion.Parse(json);

            Assert.AreEqual("Plugins/iOS/SDK", data.artifacts.ios.destination);
            CollectionAssert.AreEquivalent(RequiredFiles, data.artifacts.ios.files.Keys);
        }
    }
}
