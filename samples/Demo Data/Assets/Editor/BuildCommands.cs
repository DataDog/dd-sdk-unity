// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using UnityEditor;
using UnityEngine;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

public class BuildCommands
{
    private static string[] Scenes = new[]
        {
            "Assets/Scenes/CategoryScene.unity",
            "Assets/Scenes/CheckoutScene.unity",
            "Assets/Scenes/FirstScene.unity",
            "Assets/Scenes/ProductScene.unity"
        };

    public static void BuildAndroid()
    {
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.locationPathName = "Builds/Android.apk";
        buildPlayerOptions.scenes = Scenes;
        buildPlayerOptions.target = BuildTarget.Android;
        buildPlayerOptions.options = BuildOptions.None;
        BuildPipeline.BuildPlayer(buildPlayerOptions);
    }

    [MenuItem("Build/Build iOS")]
    public static void BuildIOS()
    {
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = Scenes;
        buildPlayerOptions.locationPathName = "Build/iOS";
        buildPlayerOptions.target = BuildTarget.iOS;
        buildPlayerOptions.options = BuildOptions.None;
        BuildPipeline.BuildPlayer(buildPlayerOptions);
    }
}
