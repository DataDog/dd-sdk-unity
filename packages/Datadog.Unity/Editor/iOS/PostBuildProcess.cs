// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.
#if UNITY_EDITOR_OSX
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using UnityEngine;

namespace Datadog.Unity.Editor.iOS
{
    public class PostBuildProcess : IPostprocessBuildWithReport
    {
        private const string DatadogBlockStart = "// > Datadog Generated Block";
        private const string DatadogBlockEnd = "// < End Datadog Generated Block";
        private static readonly string FrameworkLocation = "Packages/com.datadoghq.unity/Plugins/iOS";

        public int callbackOrder => 1;

        public void OnPostprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;
            if (target != BuildTarget.iOS)
            {
                return;
            }

            var pathToProject = report.summary.outputPath;
            Debug.Log("DatadogBuild: OnPostProcessBuild");

            try
            {
                string projectPath = PBXProject.GetPBXProjectPath(pathToProject);
                var pbxProject = new PBXProject();
                pbxProject.ReadFromFile(projectPath);

                var mainTarget = pbxProject.GetUnityMainTargetGuid();

                CopyAndAddFramework("CrashReporter.xcframework", pathToProject, pbxProject);
                CopyAndAddFramework("DatadogCore.xcframework", pathToProject, pbxProject);
                CopyAndAddFramework("DatadogLogs.xcframework", pathToProject, pbxProject);
                CopyAndAddFramework("DatadogRUM.xcframework", pathToProject, pbxProject);
                CopyAndAddFramework("DatadogInternal.xcframework", pathToProject, pbxProject);
                CopyAndAddFramework("DatadogCrashReporting.xcframework", pathToProject, pbxProject);

                var initializationFile = Path.Combine("MainApp", "DatadogInitialization.swift");
                var initializationPath = Path.Combine(pathToProject, initializationFile);
                var datadogOptions = DatadogConfigurationOptionsExtensions.GetOrCreate();
                GenerateInitializationFile(initializationPath, datadogOptions, report.summary.guid.ToString());
                var initializationFileGuid = pbxProject.AddFile(initializationFile, initializationFile, PBXSourceTree.Source);
                var swiftVersion = pbxProject.GetBuildPropertyForAnyConfig(mainTarget, "SWIFT_VERSION");
                if (string.IsNullOrEmpty(swiftVersion))
                {
                    pbxProject.AddBuildProperty(mainTarget, "SWIFT_VERSION", "5");
                }
                pbxProject.AddFileToBuild(mainTarget, initializationFileGuid);

                AddInitializationToMain(Path.Combine(pathToProject, "MainApp", "main.mm"), datadogOptions);

                if (datadogOptions.OutputSymbols)
                {
                    AddSymbolGenAndCopyToProject(pbxProject, SymbolAssemblyBuildProcess.DatadogSymbolsDir);
                }

                var projectInString = pbxProject.WriteToString();

                // Remove Bitcode. It's deprecated by Apple and Datadog doesn't support it.
                projectInString = projectInString.Replace("ENABLE_BITCODE = YES;", "ENABLE_BITCODE = NO;");

                File.WriteAllText(projectPath, projectInString);
            }
            catch (Exception e)
            {
                Debug.Log($"DatadogBuild: OnPostProcessBuild Failed: {e}");
            }
        }

        public static void CopyAndAddFramework(string frameworkName, string pathToProject, PBXProject pbxProject, bool embedAndSign = true)
        {
            var fullFrameworkPath = Path.GetFullPath(Path.Combine(FrameworkLocation, frameworkName + "~"));
            if (!Directory.Exists(fullFrameworkPath))
            {
                throw new DirectoryNotFoundException($"Could not find {fullFrameworkPath}");
            }

            var targetPath = Path.Combine(pathToProject, "Frameworks", frameworkName);
            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            Debug.Log($"Copying {fullFrameworkPath} to {targetPath}");
            FileUtil.CopyFileOrDirectory(fullFrameworkPath, targetPath);

            var relativeFrameworkPath = Path.Combine("Frameworks", frameworkName);
            var frameworkGuid = pbxProject.AddFile(relativeFrameworkPath, relativeFrameworkPath);
            var mainTarget = pbxProject.GetUnityMainTargetGuid();
            if (embedAndSign)
            {
                pbxProject.AddFileToEmbedFrameworks(mainTarget, frameworkGuid);
            }

            var buildPhase = pbxProject.GetFrameworksBuildPhaseByTarget(mainTarget);
            pbxProject.AddFileToBuildSection(mainTarget, buildPhase, frameworkGuid);
        }

        internal static void GenerateInitializationFile(string path, DatadogConfigurationOptions options, string buildId)
        {
            var env = options.Env;
            if (env is null or "")
            {
                env = "prod";
            }

            var sdkVersion = typeof(DatadogSdk).Assembly.GetName().Version?.ToString();

            var sb = new StringBuilder($@"// Datadog Options File -
// THIS FILE IS AUTO GENERATED --- changes to this file will be lost!
import Foundation
import DatadogCore
import DatadogLogs
import DatadogRUM
import DatadogCrashReporting

@_cdecl(""initializeDatadog"")
func initializeDatadog() {{
    Datadog.verbosityLevel = .debug
    var config = Datadog.Configuration(
        clientToken: ""{options.ClientToken}"",
        env: ""{env}"",
        batchSize: {GetSwiftBatchSize(options.BatchSize)},
        uploadFrequency: {GetSwiftUploadFrequency(options.UploadFrequency)}
    )
");
            var additionalConfigurationItems = new List<string>();
            if (buildId != null)
            {
                additionalConfigurationItems.Add($"            \"{DatadogSdk.ConfigKeys.BuildId}\": \"{buildId}\"");
            }

            if (sdkVersion != null)
            {
                additionalConfigurationItems.Add($"            \"{DatadogSdk.ConfigKeys.SdkVersion}\": \"{sdkVersion}\"");
            }

            var additionalConfiguration = string.Join(",\n", additionalConfigurationItems);

            sb.Append($@"
    config._internal_mutation {{
        $0.additionalConfiguration = [
{additionalConfiguration}
        ]
    }}
");

            sb.Append($@"
    Datadog.initialize(with: config, trackingConsent: .pending)

    var logsConfig = Logs.Configuration()
");
            if (options.CustomEndpoint != string.Empty)
            {
                sb.AppendLine($@"    logsConfig.customEndpoint = URL(string: ""{options.CustomEndpoint}/logs"")");
            }

            sb.AppendLine("    Logs.enable(with: logsConfig)");

            if (options.RumEnabled)
            {
                sb.Append($@"
    var rumConfig = RUM.Configuration(
        applicationID: ""{options.RumApplicationId}""
    )
");
                if (options.CustomEndpoint != string.Empty)
                {
                    sb.AppendLine($@"    rumConfig.customEndpoint = URL(string: ""{options.CustomEndpoint}/rum"")");
                }

                if (options.VitalsUpdateFrequency != VitalsUpdateFrequency.None)
                {
                    sb.AppendLine($"    rumConfig.vitalsUpdateFrequency = {GetSwiftVitalsUpdateFrequency(options.VitalsUpdateFrequency)}");
                }

                sb.AppendLine($"    rumConfig.sessionSampleRate = {options.SessionSampleRate}");
                sb.AppendLine($"    rumConfig.telemetrySampleRate = {options.TelemetrySampleRate}");
                sb.AppendLine("    RUM.enable(with: rumConfig)");
            }

            if (options.CrashReportingEnabled)
            {
                sb.AppendLine();
                sb.AppendLine("    CrashReporting.enable()");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString());
        }

        internal static void AddSymbolGenAndCopyToProject(PBXProject pbxProject, string targetDir)
        {
            const string CopyPhaseName = "Copy dSYMs for Datadog";

            var mainTarget = pbxProject.GetUnityMainTargetGuid();
            var frameworkTarget = pbxProject.GetUnityFrameworkTargetGuid();

            pbxProject.SetBuildProperty(mainTarget, "DEBUG_INFORMATION_FORMAT", "dwarf-with-dsym");
            pbxProject.SetBuildProperty(frameworkTarget, "DEBUG_INFORMATION_FORMAT", "dwarf-with-dsym");

            var buildPhases = pbxProject.GetAllBuildPhasesForTarget(mainTarget);
            if (buildPhases.Any(buildPhase => pbxProject.GetBuildPhaseName(buildPhase) == CopyPhaseName))
            {
                return;
            }

            var copyDsymScript = new StringBuilder(@$"
cd ""$BUILT_PRODUCTS_DIR""
find . -type d -name '*.dSYM' -exec cp -r '{{}}' ""$PROJECT_DIR/{SymbolAssemblyBuildProcess.DatadogSymbolsDir}/"" ';'
");

            pbxProject.AddShellScriptBuildPhase(mainTarget, CopyPhaseName, "/bin/bash", copyDsymScript.ToString());
        }

        internal static void AddInitializationToMain(string pathToMain, DatadogConfigurationOptions options)
        {
            if (!File.Exists(pathToMain))
            {
                throw new FileNotFoundException("Could not find Unity main.", pathToMain);
            }

            var mainText = new List<string>(File.ReadAllLines(pathToMain));
            mainText = RemoveDatadogBlocks(mainText);
            if (options.Enabled)
            {
                AddDatadogBlocks(mainText, options.RumEnabled);
            }

            File.WriteAllLines(pathToMain, mainText);
        }

        internal static List<string> RemoveDatadogBlocks(List<string> lines)
        {
            var newLines = new List<string>();
            bool inDatadogBlock = false;
            foreach (var line in lines)
            {
                if (line.Trim() == DatadogBlockStart)
                {
                    inDatadogBlock = true;
                }

                if (!inDatadogBlock)
                {
                    newLines.Add(line);
                }

                if (line.Trim() == DatadogBlockEnd)
                {
                    inDatadogBlock = false;
                }
            }

            return newLines;
        }

        private static void AddDatadogBlocks(List<string> lines, bool includeRum)
        {
            // Find the first blank line, insert there.
            int firstBlank = lines.FindIndex(0, x => x.Trim().Length == 0);
            lines.InsertRange(firstBlank, new string[]
            {
                    DatadogBlockStart,
                    "#import \"DatadogOptions.h\"",
                    DatadogBlockEnd,
            });

            int autoReleaseLine = lines.FindIndex(0, x => x.Trim().Contains("@autoreleasepool"));
            int insertLine = autoReleaseLine + 1;
            if (lines[insertLine].Trim() == "{")
            {
                insertLine += 1;
            }

            var newLines = new List<string>()
            {
                $"        {DatadogBlockStart}",
                "        initializeDatadog();",
                $"        {DatadogBlockEnd}",
            };

            lines.InsertRange(insertLine, newLines);
        }

        private static string GetSwiftBatchSize(BatchSize batchSize)
        {
            return batchSize switch
            {
                BatchSize.Small => ".small",
                BatchSize.Large => ".large",
                _ => ".medium",
            };
        }

        private static string GetSwiftUploadFrequency(UploadFrequency uploadFrequency)
        {
            return uploadFrequency switch
            {
                UploadFrequency.Rare => ".rare",
                UploadFrequency.Frequent => ".frequent",
                _ => ".average",
            };
        }

        private static string GetSwiftVitalsUpdateFrequency(VitalsUpdateFrequency uploadFrequency)
        {
            return uploadFrequency switch
            {
                VitalsUpdateFrequency.None => "nil",
                VitalsUpdateFrequency.Average => ".average",
                VitalsUpdateFrequency.Rare => ".rare",
                VitalsUpdateFrequency.Frequent => ".frequent",
                _ => "nil",
            };
        }
    }
}
#endif
