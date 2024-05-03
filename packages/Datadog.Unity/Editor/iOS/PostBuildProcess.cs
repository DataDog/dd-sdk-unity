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
                var frameworkTarget = pbxProject.GetUnityFrameworkTargetGuid();

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
                pbxProject.AddFileToBuild(frameworkTarget, initializationFileGuid);

                AddInitializationToAppController(Path.Combine(pathToProject, "Classes", "UnityAppController.mm"), datadogOptions);

                if (datadogOptions.OutputSymbols)
                {
                    AddSymbolGenAndCopyToProject(pbxProject, SymbolAssemblyBuildProcess.IosDatadogSymbolsDir);
                }

                // disable embed swift libs - prevents "UnityFramework.framework contains disallowed file 'Frameworks'."
                pbxProject.SetBuildProperty(frameworkTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO");

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

        internal static void GenerateInitializationFile(string path, DatadogConfigurationOptions options, string buildId)
        {
            var env = options.Env;
            if (env is null or "")
            {
                env = "prod";
            }

            var serviceName = options.ServiceName;

            var sdkVersion = DatadogSdk.SdkVersion;
            var sdkLogLevel = GetSwiftCoreLoggerLevel(options.SdkVerbosity);

            var sb = new StringBuilder($@"// Datadog Options File -
// THIS FILE IS AUTO GENERATED --- changes to this file will be lost!
import Foundation
import DatadogCore
import DatadogLogs
import DatadogRUM
import DatadogCrashReporting

@_cdecl(""initializeDatadog"")
func initializeDatadog() {{
    Datadog.verbosityLevel = {sdkLogLevel}
    var config = Datadog.Configuration(
        clientToken: ""{options.ClientToken}"",
        env: ""{env}"",
        site: {GetSwiftSite(options.Site)},
");

            if (!(serviceName is null or ""))
            {
                sb.AppendLine($"        service: \"{serviceName}\",");
            }

            sb.Append($@"        batchSize: {GetSwiftBatchSize(options.BatchSize)},
        uploadFrequency: {GetSwiftUploadFrequency(options.UploadFrequency)},
        batchProcessingLevel: {GetSwiftBatchProcessingLevel(options.BatchProcessingLevel)}
    )
");
            var additionalConfigurationItems = new List<string>()
            {
                $"           \"{DatadogSdk.ConfigKeys.Source}\": \"unity\"",
                $"           \"{DatadogSdk.ConfigKeys.NativeSourceType}\": \"ios+il2cpp\"",
            };

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
                sb.AppendLine($@"    logsConfig.customEndpoint = URL(string: ""{options.CustomEndpoint}/api/v2/logs"")");
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
                    sb.AppendLine($@"    rumConfig.customEndpoint = URL(string: ""{options.CustomEndpoint}/api/v2/rum"")");
                }

                if (options.VitalsUpdateFrequency != VitalsUpdateFrequency.None)
                {
                    sb.AppendLine($"    rumConfig.vitalsUpdateFrequency = {GetSwiftVitalsUpdateFrequency(options.VitalsUpdateFrequency)}");
                }

                sb.AppendLine($"    rumConfig.sessionSampleRate = {options.SessionSampleRate}");
                sb.AppendLine($"    rumConfig.telemetrySampleRate = {options.TelemetrySampleRate}");

                // Uncomment to enable RUM Configuration Telemetry
                // sb.AppendLine(@"    rumConfig._internal_mutation {
                //     $0.configurationTelemetrySampleRate = 100.0
                // }");
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
find . -type d -name '*.dSYM' -exec cp -r '{{}}' ""$PROJECT_DIR/{SymbolAssemblyBuildProcess.IosDatadogSymbolsDir}/"" ';'
");

            pbxProject.AddShellScriptBuildPhase(mainTarget, CopyPhaseName, "/bin/bash", copyDsymScript.ToString());
        }

        internal static void AddInitializationToAppController(string pathToMain, DatadogConfigurationOptions options)
        {
            if (!File.Exists(pathToMain))
            {
                throw new FileNotFoundException("Could not find UnityAppController.", pathToMain);
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

            int applicationLaunchLog = lines.FindIndex(0, x => x.Trim().Contains("::printf(\"-> applicationDidFinishLaunching()\\n\");"));
            int insertLine = applicationLaunchLog + 1;

            var newLines = new List<string>()
            {
                $"        {DatadogBlockStart}",
                "        initializeDatadog();",
                $"        {DatadogBlockEnd}",
            };

            lines.InsertRange(insertLine, newLines);
        }

        private static string GetSwiftCoreLoggerLevel(CoreLoggerLevel level)
        {
            return level switch
            {
                CoreLoggerLevel.Debug => ".debug",
                CoreLoggerLevel.Warn => ".warn",
                CoreLoggerLevel.Error => ".error",
                CoreLoggerLevel.Critical => ".critical",
                _ => ".warn",
            };
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

        private static string GetSwiftBatchProcessingLevel(BatchProcessingLevel batchProcessingLevel)
        {
            return batchProcessingLevel switch
            {
                BatchProcessingLevel.Low => ".low",
                BatchProcessingLevel.High => ".high",
                _ => ".medium",
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

        private static string GetSwiftSite(DatadogSite site)
        {
            return site switch
            {
                DatadogSite.Us1 => ".us1",
                DatadogSite.Us3 => ".us3",
                DatadogSite.Us5 => ".us5",
                DatadogSite.Eu1 => ".eu1",
                DatadogSite.Us1Fed => ".us1_fed",
                DatadogSite.Ap1 => ".ap1",
                _ => ".us1"
            };
        }
    }
}
#endif
