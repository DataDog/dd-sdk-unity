// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.Device;
using Debug = UnityEngine.Debug;

namespace Datadog.Unity.Editor
{
    public class SymbolAssemblyBuildProcess : IPostprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        internal const string IosDatadogSymbolsDir = "datadogSymbols";
        internal const string AndroidSymbolsDir = "symbols";

        // Relative to the output directory
        internal const string IosLineNumberMappingsLocation =
            "Il2CppOutputProject/Source/il2cppOutput/Symbols/LineNumberMappings.json";

        // Relative to the gradle output directory
        private static readonly List<string> AndroidLineNumberMappingsLocations = new ()
        {
            "../../IL2CppBackup/il2cppOutput/Symbols/LineNumberMappings.json",
            "src/main/il2CppOutputProject/Source/il2cppOutput/Symbols/LineNumberMappings.json",
        };


        // Make sure this is the last possible thing that's run
        public int callbackOrder => int.MaxValue;

        private IBuildFileSystemProxy _fileSystemProxy = new DefaultBuildFileSystemProxy();

        internal IBuildFileSystemProxy fileSystemProxy
        {
            get => _fileSystemProxy;
            set => _fileSystemProxy = value;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            var options = DatadogConfigurationOptionsExtensions.GetOrCreate();
            if (report.summary.platformGroup == BuildTargetGroup.iOS)
            {
                WriteBuildId(options, report.summary.platformGroup, report.summary.guid.ToString(), report.summary.outputPath);
            }

            CopySymbols(options, report.summary.platformGroup, report.summary.guid.ToString(),
                    report.summary.outputPath);
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var options = DatadogConfigurationOptionsExtensions.GetOrCreate();
            // Since Unity doesn't give us a copy of a buildGUID at this stage, generate our own.
            var buildGuid = Guid.NewGuid().ToString();
            WriteBuildId(options, BuildTargetGroup.Android, buildGuid, path);
            AndroidCopyMappingFile(options, path);
        }

        internal void AndroidCopyMappingFile(DatadogConfigurationOptions options, string gradleRoot)
        {
            // If the project is not configured to output symbols, do nothing
            if (!options.Enabled || !options.OutputSymbols)
            {
                return;
            }

            // Check all known paths where LineNumberMappings.json might be stored, relative
            // to the root directory of our gradle project
            string srcFilePath = null;
            foreach (var relativePath in AndroidLineNumberMappingsLocations)
            {
                var candidateFilePath = Path.Join(gradleRoot, relativePath);
                if (_fileSystemProxy.FileExists(candidateFilePath))
                {
                    srcFilePath = candidateFilePath;
                    break;
                }
            }

            // If we failed to find the file, abort
            if (string.IsNullOrEmpty(srcFilePath))
            {
                Debug.LogWarning("Could not find IL2CPP mappings file.");
                return;
            }

            // We've found the file: copy it to our canonical destination path
            var dstDirPath = Path.Join(gradleRoot, AndroidSymbolsDir);
            var dstFilePath = Path.Join(dstDirPath, Path.GetFileName(srcFilePath));
            try
            {
                if (!_fileSystemProxy.DirectoryExists(dstDirPath))
                {
                    _fileSystemProxy.CreateDirectory(dstDirPath);
                }

                Debug.Log("Copying IL2CPP mappings file...");
                _fileSystemProxy.CopyFile(srcFilePath, dstFilePath);
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to copy IL2CPP mappings file");
                Debug.LogException(e);
            }
        }

        internal void WriteBuildId(DatadogConfigurationOptions options,
            BuildTargetGroup platformGroup,
            string buildGuid,
            string outputPath)
        {
            if (platformGroup is not (BuildTargetGroup.Android or BuildTargetGroup.iOS))
            {
                // Only copy symbols for Android and iOS
                return;
            }

            if (options.Enabled && options.OutputSymbols)
            {
                var outputDir = platformGroup switch
                {
                    BuildTargetGroup.Android => AndroidSymbolsDir,
                    BuildTargetGroup.iOS => IosDatadogSymbolsDir,
                    _ => ""
                };

                var symbolsDir = Path.Join(outputPath, outputDir);
                if (!_fileSystemProxy.DirectoryExists(symbolsDir))
                {
                    _fileSystemProxy.CreateDirectory(symbolsDir);
                }

                var buildIdPath = Path.Join(symbolsDir, "build_id");
                _fileSystemProxy.WriteAllText(buildIdPath, buildGuid);

                if (platformGroup == BuildTargetGroup.Android)
                {
                    // Write the build id to the Android assets directory
                    var androidAssetsDir = Path.Join(outputPath, "src/main/assets");
                    var androidBuildIdPath = Path.Join(androidAssetsDir, "datadog.buildId");
                    _fileSystemProxy.WriteAllText(androidBuildIdPath, buildGuid);
                }
            }
        }

        internal void CopySymbols(DatadogConfigurationOptions options, BuildTargetGroup platformGroup, string buildGuid, string outputPath)
        {
            if (platformGroup is not (BuildTargetGroup.Android or BuildTargetGroup.iOS))
            {
                // Only copy symbols for Android and iOS
                return;
            }

            if (options.Enabled && options.OutputSymbols)
            {
                switch (platformGroup)
                {
                    case BuildTargetGroup.iOS:
                        CopyIosSymbolFiles(outputPath);
                        break;
                    default:
                        break;
                }
            }
        }

        private void CopyIosSymbolFiles(string outputPath)
        {
            var mappingsSrcPath = Path.Join(outputPath, IosLineNumberMappingsLocation);
            var mappingsDestPath = Path.Join(outputPath, IosDatadogSymbolsDir, "LineNumberMappings.json");
            if (_fileSystemProxy.FileExists(mappingsSrcPath))
            {
                Debug.Log("Copying IL2CPP mappings file...");
                if (_fileSystemProxy.FileExists(mappingsDestPath))
                {
                    _fileSystemProxy.DeleteFile(mappingsDestPath);
                }
                _fileSystemProxy.CopyFile(mappingsSrcPath, mappingsDestPath);
            }
            else
            {
                Debug.LogWarning("Could not find iOS IL2CPP mappings file.");
            }
        }
    }
}
