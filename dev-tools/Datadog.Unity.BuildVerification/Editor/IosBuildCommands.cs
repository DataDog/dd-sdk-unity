// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.
//
// Batch-mode iOS build entry point for tools/scripts/verify_ios_build.py. Lives in a
// dev-only local package (not referenced by the published com.datadoghq.unity package)
// so it never ships to consumers. Unity CLI selects the iOS target before invoking it.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Datadog.Unity.BuildVerification
{
    /// <summary>
    /// Batch-mode entry point invoked via -executeMethod by tools/scripts/verify_ios_build.py
    /// to build the active Unity project for iOS and report a machine-parseable result.
    /// </summary>
    public class IosBuildCommands
    {
        public static void BuildIOS()
        {
            string[] args = Environment.GetCommandLineArgs();
            int outputIndex = Array.IndexOf(args, "-buildOutput");
            if (outputIndex < 0 || outputIndex + 1 >= args.Length)
            {
                Debug.LogError("DatadogIosBuild: No build output path specified.");
                EditorApplication.Exit(1);
                return;
            }
            string outputDirectory = args[outputIndex + 1];

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                string scenesDir = Path.Combine("Assets", "Scenes");
                if (Directory.Exists(scenesDir))
                {
                    string fallbackScene = Directory.GetFiles(scenesDir, "*.unity").FirstOrDefault();
                    if (fallbackScene != null)
                    {
                        scenes = new[] { fallbackScene };
                    }
                }
            }

            Debug.Log($"DatadogIosBuild: Building with {scenes.Length} scene(s): {string.Join(", ", scenes)}");

            var buildPlayerOptions = new BuildPlayerOptions
            {
                target = BuildTarget.iOS,
                locationPathName = outputDirectory,
                scenes = scenes,
                options = BuildOptions.CleanBuildCache,
            };

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);

            Debug.Log($"DatadogIosBuild: Build result: {report.summary.result}");
            Debug.Log($"DatadogIosBuild: Total errors: {report.summary.totalErrors}");

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"DatadogIosBuild: Build failed:\n{DescribeBuildErrors(report)}");
                EditorApplication.Exit(1);
                return;
            }

            string absoluteOutputPath = Path.GetFullPath(outputDirectory);

            Debug.Log($"DatadogIosBuild: result=succeeded errors={report.summary.totalErrors} output={absoluteOutputPath}");
            EditorApplication.Exit(0);
        }

        // BuildReport's summarize-errors convenience method is not available on Unity
        // 2021.3's Editor API (it causes a compile error there); this manual walk over
        // report.steps/messages is stable across 2021.3, 2022.3, and 6000.2, so it is
        // used here instead.
        private static string DescribeBuildErrors(BuildReport report)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                    {
                        builder.AppendLine($"[{step.name}] {message.content}");
                    }
                }
            }

            return builder.Length > 0 ? builder.ToString() : "(no per-step error messages captured)";
        }
    }
}
