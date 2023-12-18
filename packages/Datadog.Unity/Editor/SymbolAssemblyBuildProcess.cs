// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Datadog.Unity.Editor
{
    public class SymbolAssemblyBuildProcess : IPostprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        internal const string DatadogSymbolsDir = "datadogSymbols";
        internal const string AndroidLineNumberMappingsOutputPath = "build/symbols";

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
            CopySymbols(options, report.summary.platformGroup, report.summary.guid.ToString(), report.summary.outputPath);
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var options = DatadogConfigurationOptionsExtensions.GetOrCreate();
            AndroidCopyMappingFile(options, path);
        }

        internal void AndroidCopyMappingFile(DatadogConfigurationOptions options, string path)
        {
            if (!options.Enabled || !options.OutputSymbols)
            {
                return;
            }

            bool foundFile = false;
            try
            {
                // Find the line number mapping file and copy it to the proper location
                // for the Datadog Gradle plugin to find
                foreach (var mappingLocation in AndroidLineNumberMappingsLocations)
                {
                    var mappingsSrcPath = Path.Join(path, mappingLocation);
                    if (_fileSystemProxy.FileExists(mappingsSrcPath))
                    {
                        var mappingsDestPath = Path.Join(path, AndroidLineNumberMappingsOutputPath);
                        if (!_fileSystemProxy.DirectoryExists(mappingsDestPath))
                        {
                            _fileSystemProxy.CreateDirectory(mappingsDestPath);
                        }

                        Debug.Log("Copying IL2CPP mappings file...");
                        _fileSystemProxy.CopyFile(mappingsSrcPath, mappingsDestPath);
                        foundFile = true;
                        break;
                    }
                }

                if (!foundFile)
                {
                    Debug.LogWarning("Could not find IL2CPP mappings file.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to copy IL2CPP mappings file");
                Debug.LogException(e);
            }
        }

        internal void CopySymbols(DatadogConfigurationOptions options, BuildTargetGroup platformGroup, string buildGuid, string outputPath)
        {
            if (options.Enabled && options.OutputSymbols)
            {
                // Only iOS for now, but might change in the future
                var shouldOutputBuildId = platformGroup == BuildTargetGroup.iOS;

                if (shouldOutputBuildId)
                {
                    var symbolsDir = Path.Join(outputPath, DatadogSymbolsDir);
                    if (!_fileSystemProxy.DirectoryExists(symbolsDir))
                    {
                        _fileSystemProxy.CreateDirectory(symbolsDir);
                    }

                    var buildIdPath = Path.Join(symbolsDir, "build_id");
                    _fileSystemProxy.WriteAllText(buildIdPath, buildGuid);
                }

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
            var mappingsDestPath = Path.Join(outputPath, DatadogSymbolsDir, "LineNumberMappings.json");
            if (_fileSystemProxy.FileExists(mappingsSrcPath))
            {
                Debug.Log("Copying IL2CPP mappings file...");
                _fileSystemProxy.CopyFile(mappingsSrcPath, mappingsDestPath);
            }
            else
            {
                Debug.LogWarning("Could not find iOS IL2CPP mappings file.");
            }
        }
    }
}
