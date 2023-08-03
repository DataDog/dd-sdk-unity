// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using UnityEngine;

namespace Datadog.Unity.Editor.iOS
{
    public static class PostBuildProcess
    {
        private const string DatadogBlockStart = "// > Datadog Generated Block";
        private const string DatadogBlockEnd = "// < End Datadog Generated Block";
        private static readonly string FrameworkLocation = "Packages/com.datadoghq.unity/Plugins/iOS";

        [PostProcessBuild(1)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            Debug.Log("DatadogBuild: OnPostProcessBuild");

            try
            {
                string projectPath = PBXProject.GetPBXProjectPath(pathToProject);
                var pbxProject = new PBXProject();
                pbxProject.ReadFromFile(projectPath);

                var mainTarget = pbxProject.GetUnityMainTargetGuid();
                pbxProject.AddBuildProperty(mainTarget, "OTHER_LDFLAGS", "-ObjC");

                CopyAndAddFramework("Datadog.xcframework", pathToProject, pbxProject);
                CopyAndAddFramework("DatadogObjc.xcframework", pathToProject, pbxProject);
                CopyAndAddFramework("DatadogCrashReporting.xcframework", pathToProject, pbxProject);

                var optionsFile = Path.Combine("MainApp", "DatadogOptions.m");
                var optionsPath = Path.Combine(pathToProject, optionsFile);
                var datadogOptions = DatadogConfigurationOptionsExtensions.GetOrCreate();
                GenerateOptionsFile(optionsPath, datadogOptions);
                var optionsFileGuid = pbxProject.AddFile(optionsFile, optionsFile, PBXSourceTree.Source);
                pbxProject.AddFileToBuild(mainTarget, optionsFileGuid);

                AddInitializationToMain(Path.Combine(pathToProject, "MainApp", "main.mm"), datadogOptions);

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

        internal static void GenerateOptionsFile(string path, DatadogConfigurationOptions options)
        {
            var customEndpointString = string.Empty;
            if (options.CustomEndpoint != string.Empty)
            {
                customEndpointString = $@"
    [builder setWithCustomLogsEndpoint:[[NSURL alloc] initWithString:@""{options.CustomEndpoint}/logs""]];
    [builder setWithCustomRUMEndpoint:[[NSURL alloc] initWithString:@""{options.CustomEndpoint}/rum""]];
";
            }

            var builderSetup = $@"
    DDConfigurationBuilder *builder = [DDConfiguration builderWithClientToken:@""{options.ClientToken}""
                                                                  environment:@""prod""];
";
            if (options.RumEnabled && options.RumApplicationId != string.Empty)
            {
                builderSetup = $@"
    DDConfigurationBuilder *builder = [DDConfiguration builderWithRumApplicationID:@""{options.RumApplicationId}""
                                                                       clientToken:@""{options.ClientToken}""
                                                                       environment:@""prod""];
";
            }

            var optionsFileString = $@"// Datadog Options File -
// THIS FILE IS AUTO GENERATED --- changes to this file will be lost!
#include <Datadog/Datadog-Swift.h>
#include <DatadogObjc/DatadogObjc-Swift.h>
#include <DatadogCrashReporting/DatadogCrashReporting-Swift.h>

DDConfiguration* buildDatadogConfiguration() {{
    [DDDatadog setVerbosityLevel:DDSDKVerbosityLevelDebug];
    {builderSetup}

    [builder enableTracing:NO];
    [builder enableCrashReportingUsing:[DDCrashReportingPlugin new]];
    [builder setWithBatchSize:{GetObjCBatchSize(options.BatchSize)}];
    [builder setWithUploadFrequency:{GetObjCUploadFrequency(options.UploadFrequency)}];
    {customEndpointString}

    return [builder build];
}}
";
            File.WriteAllText(path, optionsFileString);
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
                    "#import <Datadog/Datadog-Swift.h>",
                    "#import <DatadogObjc/DatadogObjc-Swift.h>",
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
                "        [DDDatadog initializeWithAppContext:[DDAppContext new]",
                "                            trackingConsent:[DDTrackingConsent pending]",
                "                              configuration:buildDatadogConfiguration()];",
            };

            if (includeRum)
            {
                newLines.Add(string.Empty);
                newLines.Add("        DDGlobal.rum = [[DDRUMMonitor alloc] init];");
            }
            newLines.Add($"        {DatadogBlockEnd}");

            lines.InsertRange(insertLine, newLines);
        }

        private static string GetObjCBatchSize(BatchSize batchSize)
        {
            return batchSize switch
            {
                BatchSize.Small => "DDBatchSizeSmall",
                BatchSize.Large => "DDBatchSizeLarge",
                _ => "DDBatchSizeMedium",
            };
        }

        private static string GetObjCUploadFrequency(UploadFrequency uploadFrequency)
        {
            return uploadFrequency switch
            {
                UploadFrequency.Rare => "DDUploadFrequencyRare",
                UploadFrequency.Frequent => "DDUploadFrequencyFrequent",
                _ => "DDUploadFrequencyAverage",
            };
        }
    }
}
