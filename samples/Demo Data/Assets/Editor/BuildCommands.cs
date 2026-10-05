// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System;
using UnityEditor;
using UnityEngine;
using UnityEditor.Build.Reporting;

public class BuildCommands
{
    private static readonly string[] Scenes =
    {
        "Assets/Scenes/CategoryScene.unity",
        "Assets/Scenes/CheckoutScene.unity",
        "Assets/Scenes/FirstScene.unity",
        "Assets/Scenes/ProductScene.unity"
    };

    public static void BuildHeadless()
    {
        string[] args = Environment.GetCommandLineArgs();
        int outputIndex = Array.IndexOf(args, "-buildOutput");
        if (outputIndex < 0 || outputIndex + 1 >= args.Length)
        {
            Debug.LogError("No build output path specified.");
            EditorApplication.Exit(1);
            return;
        }

        Build(EditorUserBuildSettings.activeBuildTarget, args[outputIndex + 1], true);
    }

    [MenuItem("Build/Build Android")]
    public static void BuildAndroid()
    {
        Build(BuildTarget.Android, "Build/Android/datadog-demo.apk", false);
    }

    [MenuItem("Build/Build iOS")]
    public static void BuildIOS()
    {
        Build(BuildTarget.iOS, "Build/iOS", false);
    }

    private static void Build(BuildTarget target, string outputLocation, bool exitOnError)
    {
        Debug.Log($"Building for {target}: {outputLocation}");

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.locationPathName = outputLocation;
        buildPlayerOptions.scenes = Scenes;
        buildPlayerOptions.target = target;
        buildPlayerOptions.options = BuildOptions.CleanBuildCache;

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build OK: {outputLocation}");
        }
        else
        {
            Debug.LogError($"Build Failed:\n{report.SummarizeErrors()}");
            if (exitOnError)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
