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
        private static readonly string[] RequiredModules = new[]
        {
            "DatadogCore", "DatadogInternal", "DatadogLogs", "DatadogRUM", "DatadogCrashReporting",
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

        private void CreateModuleDirectories(params string[] modules)
        {
            foreach (var module in modules)
            {
                Directory.CreateDirectory(Path.Combine(_tempDirectory, $"{module}.xcframework"));
            }
        }

        [Test]
        public void FindMissingModulesReturnsEmptyWhenAllModulesPresent()
        {
            CreateModuleDirectories(RequiredModules);

            var missing = IosXcframeworkPreprocessBuild.FindMissingModules(_tempDirectory, RequiredModules);

            Assert.IsEmpty(missing);
        }

        [Test]
        public void FindMissingModulesReturnsSingleMissingModule()
        {
            CreateModuleDirectories(RequiredModules.Take(4).ToArray());

            var missing = IosXcframeworkPreprocessBuild.FindMissingModules(_tempDirectory, RequiredModules);

            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual(RequiredModules[4], missing[0]);
        }

        [Test]
        public void FindMissingModulesReturnsAllModulesWhenDirectoryDoesNotExist()
        {
            var nonExistentDirectory = Path.Combine(_tempDirectory, "does-not-exist");

            var missing = IosXcframeworkPreprocessBuild.FindMissingModules(nonExistentDirectory, RequiredModules);

            Assert.AreEqual(RequiredModules.Length, missing.Count);
            foreach (var module in RequiredModules)
            {
                Assert.Contains(module, missing);
            }
        }

        [Test]
        public void FindMissingModulesReportsModuleMissingWhenPathIsAFileNotDirectory()
        {
            CreateModuleDirectories(RequiredModules.Skip(1).ToArray());
            File.WriteAllText(Path.Combine(_tempDirectory, $"{RequiredModules[0]}.xcframework"), "not a directory");

            var missing = IosXcframeworkPreprocessBuild.FindMissingModules(_tempDirectory, RequiredModules);

            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual(RequiredModules[0], missing[0]);
        }

        [Test]
        public void ValidateThrowsBuildFailedExceptionWhenModulesMissing()
        {
            CreateModuleDirectories(RequiredModules.Take(3).ToArray());
            var absoluteDir = Path.GetFullPath(_tempDirectory);

            var ex = Assert.Throws<BuildFailedException>(
                () => IosXcframeworkPreprocessBuild.Validate(absoluteDir, RequiredModules));

            Assert.IsTrue(ex.Message.Contains(RequiredModules[3]));
            Assert.IsTrue(ex.Message.Contains(RequiredModules[4]));
            Assert.IsTrue(ex.Message.Contains(absoluteDir));
            Assert.IsTrue(ex.Message.Contains("./run-script ios_xcframework"));
        }

        [Test]
        public void ValidateDoesNotThrowWhenAllModulesPresent()
        {
            CreateModuleDirectories(RequiredModules);
            var absoluteDir = Path.GetFullPath(_tempDirectory);

            Assert.DoesNotThrow(() => IosXcframeworkPreprocessBuild.Validate(absoluteDir, RequiredModules));
        }

        [Test]
        public void IosDependencyVersionParseYieldsExpectedFiveModules()
        {
            var json = @"{
  ""version"": ""3.11.1"",
  ""sha256"": ""9fe66c4b4c4e3ba68b253c701aff97447358b08b0c5b43af6d4854bf1563c13d"",
  ""modules"": [
    ""DatadogCore"",
    ""DatadogInternal"",
    ""DatadogLogs"",
    ""DatadogRUM"",
    ""DatadogCrashReporting""
  ]
}";

            var data = IosDependencyVersion.Parse(json);

            Assert.AreEqual(5, data.modules.Length);
            foreach (var module in RequiredModules)
            {
                Assert.Contains(module, data.modules);
            }
        }
    }
}
