// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using UnityEngine;

namespace Datadog.Unity.Editor.iOS
{
    public static class DatadogBuildProcess
    {
        private const string k_datadogBlockStart = "// > Datadog Generated Block";
        private const string k_datadogBlockEnd = "// < End Datadog Generated Block";
        private static string s_frameworkLocation = "Packages/com.datadoghq.unity/Plugins/iOS";


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
                CopyAndAddFramework("CrashReporter.xcframework", pathToProject, pbxProject, false);

                var optionsFile = Path.Combine("MainApp", "DatadogOptions.m");
                var optionsPath = Path.Combine(pathToProject, optionsFile);
                GenerateOptionsFile(optionsPath, DatadogConfigurationOptionsExtensions.GetOrCreate());
                var optionsFileGuid = pbxProject.AddFile(optionsFile, optionsFile, PBXSourceTree.Source);
                pbxProject.AddFileToBuild(mainTarget, optionsFileGuid);

                AddInitializationToMain(Path.Combine(pathToProject, "MainApp", "main.mm"));

                pbxProject.WriteToFile(projectPath);
            }
            catch (Exception e)
            {
                Debug.Log($"DatadogBuild: OnPostProcessBuild Failed: {e}");
            }
        }

        public static void CopyAndAddFramework(string frameworkName, string pathToProject, PBXProject pbxProject, bool embedAndSign = true)
        {
            var fullFrameworkPath = Path.GetFullPath(Path.Combine(s_frameworkLocation, frameworkName + "~"));
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

        private static void GenerateOptionsFile(string path, DatadogConfigurationOptions options)
        {
            var optionsFileString = $@"// Datadog Options File -
// THIS FILE IS AUTO GENERATED --- changes to this file will be lost!
#include <Datadog/Datadog-Swift.h>
#include <DatadogObjc/DatadogObjc-Swift.h>
#include <DatadogCrashReporting/DatadogCrashReporting-Swift.h>

DDConfiguration* buildDatadogConfiguration() {{
    DDConfigurationBuilder *builder = [DDConfiguration builderWithClientToken:@""{options.ClientToken}""
                                                                  environment:@""prod""];
    [builder enableTracing:NO];
    [builder enableCrashReportingUsing:[DDCrashReportingPlugin new]];

    return [builder build];
}}
";
            File.WriteAllText(path, optionsFileString);
        }

        private static void AddInitializationToMain(string pathToMain)
        {
            if (!File.Exists(pathToMain))
            {
                throw new FileNotFoundException("Could not find Unity main.", pathToMain);
            }

            var mainText = new List<string>(File.ReadAllLines(pathToMain));
            mainText = RemoveDatadogBlocks(mainText);
            AddDatadogBlocks(mainText);

            File.WriteAllLines(pathToMain, mainText);
        }

        private static List<string> RemoveDatadogBlocks(List<string> lines)
        {
            var newLines = new List<String>();
            bool inDatadogBlock = false;
            foreach (var line in lines)
            {
                if (line.Trim() == k_datadogBlockStart)
                {
                    inDatadogBlock = true;
                }

                if (!inDatadogBlock)
                {
                    newLines.Add(line);
                }

                if (line.Trim() == k_datadogBlockEnd)
                {
                    inDatadogBlock = false;
                }
            }

            return newLines;
        }

        private static void AddDatadogBlocks(List<string> lines)
        {
            // Find the first blank line, insert there.
            int firstBlank = lines.FindIndex(0, x => x.Trim().Length == 0);
            lines.InsertRange(firstBlank, new string[] {
                k_datadogBlockStart,
                "#import <Datadog/Datadog-Swift.h>",
                "#import <DatadogObjc/DatadogObjc-Swift.h>",
                "#import \"DatadogOptions.h\"",
                k_datadogBlockEnd
            });

            int autoReleaseLine = lines.FindIndex(0, x => x.Trim().Contains("@autoreleasepool"));
            int insertLine = autoReleaseLine + 1;
            if (lines[insertLine].Trim() == "{")
            {
                insertLine += 1;
            }

            lines.InsertRange(insertLine, new string[] {
                $"        {k_datadogBlockStart}",
                "        [DDDatadog initializeWithAppContext:[DDAppContext new]",
                "                            trackingConsent:[DDTrackingConsent pending]",
                "                              configuration:buildDatadogConfiguration()];",
                $"        {k_datadogBlockEnd}",
            });
        }
    }
}