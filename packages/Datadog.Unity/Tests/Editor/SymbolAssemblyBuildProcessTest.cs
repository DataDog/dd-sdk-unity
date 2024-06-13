// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.IO;
using System.Linq;
using NSubstitute;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Datadog.Unity.Editor.Tests
{
    public class SymbolAssemblyBuildProcessTest
    {
        private IBuildFileSystemProxy _fileSystemProxy;
        private SymbolAssemblyBuildProcess _process;

        [SetUp]
        public void SetUp()
        {
            _fileSystemProxy = Substitute.For<IBuildFileSystemProxy>();
            _process = new SymbolAssemblyBuildProcess();
            _process.fileSystemProxy = _fileSystemProxy;
        }

        [Test]
        public void DoesNothingWithDatadogDisabled()
        {
            // Given
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = false;

            // When
            _process.CopySymbols(options, BuildTargetGroup.iOS, "fake-guid", "fake-output-path");

            // Then
            Assert.IsFalse(_fileSystemProxy.ReceivedCalls().Any());
        }

        [Test]
        public void DoesNothingWithOutputSymbolsDisabled()
        {
            // Given
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.OutputSymbols = false;

            // When
            _process.CopySymbols(options, BuildTargetGroup.iOS, "fake-guid", "fake-output-path");

            // Then
            Assert.IsFalse(_fileSystemProxy.ReceivedCalls().Any());
        }

        [Test]
        public void WritesGuidToSymbolsPathWhenIos()
        {
            // Given
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.OutputSymbols = true;

            // When
            _process.WriteBuildId(options, BuildTargetGroup.iOS, "fake-guid", "fake-output-path");

            // Then
            var expectedPath = Path.Join("fake-output-path", SymbolAssemblyBuildProcess.IosDatadogSymbolsDir, "build_id");
            _fileSystemProxy.Received(1).WriteAllText(expectedPath, "fake-guid");
        }

        [Test]
        public void WritesGuidToSymbolsPathWhenAndroid()
        {
            // Given
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.OutputSymbols = true;

            // When
            _process.WriteBuildId(options, BuildTargetGroup.Android, "fake-guid", "fake-output-path");

            // Then
            var expectedPath = Path.Join("fake-output-path", SymbolAssemblyBuildProcess.AndroidSymbolsDir, "build_id");
            _fileSystemProxy.Received(1).WriteAllText(expectedPath, "fake-guid");
        }

        [Test]
        public void WritesGuidToAndroidBuildWhenAndroid()
        {
            // Given
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.OutputSymbols = true;

            // When
            _process.WriteBuildId(options, BuildTargetGroup.Android, "fake-guid", "fake-output-path");

            // Then
            var expectedPath = Path.Join("fake-output-path", "src/main/assets", "datadog.buildId");
            _fileSystemProxy.Received(1).WriteAllText(expectedPath, "fake-guid");
        }

        [Test]
        public void CopiesIL2CPPFileToOutputPathWhenIos()
        {
            // Given
            var il2cppPath = Path.Join("fake-output-path", SymbolAssemblyBuildProcess.IosLineNumberMappingsLocation);
            _fileSystemProxy.FileExists(il2cppPath).Returns(true);

            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.OutputSymbols = true;

            // When
            _process.CopySymbols(options, BuildTargetGroup.iOS, "fake-guid", "fake-output-path");

            // Then
            var expectedPath = Path.Join("fake-output-path",
                SymbolAssemblyBuildProcess.IosDatadogSymbolsDir,
                "LineNumberMappings.json"
                );
            _fileSystemProxy.Received(1).CopyFile(il2cppPath, expectedPath);
        }

        [Test]
        public void CopySymbolsDoesNothingForAndroid()
        {
            // NOTE: This is because we have to wait until the gradle project is generated, which
            // is a separate call than the one for CopySymbols.

            // Given
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.OutputSymbols = true;

            // When
            _process.CopySymbols(options, BuildTargetGroup.Android, "fake-guid", "fake-output-path");

            // Then
            Assert.IsFalse(_fileSystemProxy.ReceivedCalls().Any());
        }

        [Test]
        public void AndroidCopyMappingDoesNothingWithDatadogDisabled()
        {
            // Given
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = false;

            // When
            _process.AndroidCopyMappingFile(options, "fake-output-path");

            // Then
            Assert.IsFalse(_fileSystemProxy.ReceivedCalls().Any());
        }

        [Test]
        public void AndroidCopyMappingDoesNothingWithCopySymbolsDisabled()
        {
            // Given
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.OutputSymbols = false;

            // When
            _process.AndroidCopyMappingFile(options, "fake-output-path");

            // Then
            Assert.IsFalse(_fileSystemProxy.ReceivedCalls().Any());
        }

        [Test]
        [TestCase("../../IL2CppBackup/il2cppOutput/Symbols/LineNumberMappings.json")]
        [TestCase("src/main/il2CppOutputProject/Source/il2cppOutput/Symbols/LineNumberMappings.json")]
        public void AndroidCopyMappingCopiesFileIfExists(string possiblePath)
        {
            // Given
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.OutputSymbols = true;
            var testPath = Path.Join("fake-output-path", possiblePath);
            _fileSystemProxy.FileExists(testPath).Returns(true);

            // When
            _process.AndroidCopyMappingFile(options, "fake-output-path");

            // Then
            var expectedPath = Path.Join("fake-output-path", "symbols");
            _fileSystemProxy.Received(1).CopyFile(testPath, expectedPath);
        }
    }
}
