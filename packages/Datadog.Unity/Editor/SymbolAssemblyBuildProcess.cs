// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Datadog.Unity.Editor
{
    public class SymbolAssemblyBuildProcess : IPostprocessBuildWithReport
    {
        public const string DatadogSymbolsDir = "datadogSymbols";

        private const string IosLineNumberMappingsLocation =
            "Il2CppOutputProject/Source/il2cppOutput/Symbols/LineNumberMappings.json";

        // Make sure this is the last possible thing that's run
        public int callbackOrder => int.MaxValue;

        public void OnPostprocessBuild(BuildReport report)
        {
            var options = DatadogConfigurationOptionsExtensions.GetOrCreate();
            if (options.Enabled && options.OutputSymbols)
            {
                var symbolsDir = Path.Join(report.summary.outputPath, DatadogSymbolsDir);
                if (!Directory.Exists(symbolsDir))
                {
                    Directory.CreateDirectory(symbolsDir);
                }

                var buildIdPath = Path.Join(symbolsDir, "build_id");
                File.WriteAllText(buildIdPath, report.summary.guid.ToString());

                switch (report.summary.platformGroup)
                {
                    case BuildTargetGroup.iOS:
                        CopyIosSymbolFiles(report);
                        break;
                    default:
                        break;
                }
            }
        }

        private void CopyIosSymbolFiles(BuildReport report)
        {
            var mappingsSrcPath = Path.Join(report.summary.outputPath, IosLineNumberMappingsLocation);
            var mappingsDestPath = Path.Join(report.summary.outputPath, DatadogSymbolsDir, "LineNumberMappings.json");
            if (File.Exists(mappingsSrcPath))
            {
                Debug.Log("Copying IL2CPP mappings file...");
                File.Copy(mappingsSrcPath, mappingsDestPath);
            }
            else
            {
                Debug.LogWarning("Could not find iOS IL2CPP mappings file.");
            }
        }
    }
}
