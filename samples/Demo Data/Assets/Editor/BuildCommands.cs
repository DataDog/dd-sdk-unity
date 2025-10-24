// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System;
using System.ComponentModel;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

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
        // Parse options from command-line args supplied to Unity editor binary
        string[] args = Environment.GetCommandLineArgs();
        BuildTarget? buildPlatform = null;
        string buildOutputDirectory = string.Empty;
        for (int i = 0; i < args.Length; ++i)
        {
            if (args[i] == "-buildPlatform" && i + 1 < args.Length)
            {
                string platformString = args[i + 1].ToLower();
                if (platformString == "android")
                {
                    buildPlatform = BuildTarget.Android;
                }
                else if (platformString == "ios")
                {
                    buildPlatform = BuildTarget.iOS;
                }
            }

            if (args[i] == "-buildOutputDirectory" && i + 1 < args.Length)
            {
                buildOutputDirectory = args[i + 1];
            }
        }

        // Require that we got a recognized target platform
        if (buildPlatform == null)
        {
            Debug.LogError("No supported build platform specified.");
            EditorApplication.Exit(1);
            return;
        }

        // Use the default output directory if not overridden
        if (buildOutputDirectory == string.Empty)
        {
            buildOutputDirectory = GetDefaultOutputDirectory(buildPlatform.Value);
        }

        // Invoke our build, exiting Unity if unsuccessful
        Build(buildPlatform.Value, buildOutputDirectory, true);
    }

    [MenuItem("Build/Build Android")]
    public static void BuildAndroid()
    {
        BuildInEditor(BuildTarget.Android);
    }

    [MenuItem("Build/Build iOS")]
    public static void BuildIOS()
    {
        BuildInEditor(BuildTarget.iOS);
    }

    private static void BuildInEditor(BuildTarget target)
    {
        Build(target, GetDefaultOutputDirectory(target), false);
    }

    private static void Build(BuildTarget target, string outputDirectory, bool exitOnError)
    {
        string outputLocation = GetOutputLocation(target, outputDirectory);

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

    private static string GetDefaultOutputDirectory(BuildTarget target)
    {
        switch (target)
        {
            case BuildTarget.Android:
                return "Build/Android";
            case BuildTarget.iOS:
                return "Build/iOS";
        }
        throw new InvalidEnumArgumentException();
    }

    private static string GetOutputLocation(BuildTarget target, string directory)
    {
        switch (target)
        {
            case BuildTarget.Android:
                return Path.Join(directory, "datadog-demo.apk");
            case BuildTarget.iOS:
                return directory;
        }
        throw new InvalidEnumArgumentException();
    }
}
