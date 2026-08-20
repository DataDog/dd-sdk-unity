// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using UnityEditor;
using UnityEngine;

// NuGetForUnity installs the Castle.Core/NSubstitute test-only plugin DLLs below unrestricted
// (isExplicitlyReferenced: 0, "Any" platform enabled), which is harmless on Unity 2022.3/6000.2
// but makes Unity 2021.3's "Extracting referenced dlls" step reject Castle.Core's
// System.Diagnostics.EventLog reference when switching to an iOS Player build target. Restricting
// these DLLs to Editor-only via the PluginImporter API (never hand-edited YAML) removes them from
// every Player build target while leaving the EditMode test assemblies' explicit
// precompiledReferences resolution (NSubstitute.dll) untouched. Re-run
// RestrictTestPluginsToEditor if NuGetForUnity re-adds or upgrades any of the listed packages.
public static class DatadogScaffoldPluginSettings
{
    private static readonly string[] TestOnlyPluginPaths = new[]
    {
        "Assets/Packages/Castle.Core.5.2.1/lib/netstandard2.1/Castle.Core.dll",
        "Assets/Packages/NSubstitute.5.3.0/lib/netstandard2.0/NSubstitute.dll",
        "Assets/Packages/NSubstitute.Analyzers.CSharp.1.0.17/analyzers/dotnet/cs/NSubstitute.Analyzers.CSharp.dll",
        "Assets/Packages/NSubstitute.Analyzers.CSharp.1.0.17/analyzers/dotnet/cs/NSubstitute.Analyzers.Shared.dll",
    };

    private static readonly BuildTarget[] RestrictedPlatforms = new[]
    {
        BuildTarget.iOS,
        BuildTarget.Android,
        BuildTarget.StandaloneOSX,
        BuildTarget.StandaloneWindows64,
        BuildTarget.StandaloneLinux64,
        BuildTarget.WebGL,
    };

    [MenuItem("Datadog/Scaffold/Restrict Test Plugins To Editor")]
    public static void RestrictTestPluginsToEditor()
    {
        var updated = 0;
        foreach (var path in TestOnlyPluginPaths)
        {
            var importer = PluginImporter.GetAtPath(path) as PluginImporter;
            if (importer == null)
            {
                Debug.LogError($"DatadogScaffoldPluginSettings: could not find a PluginImporter at {path}");
                continue;
            }

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            foreach (var platform in RestrictedPlatforms)
            {
                importer.SetCompatibleWithPlatform(platform, false);
            }

            importer.SaveAndReimport();
            Debug.Log($"DatadogScaffoldPluginSettings: restricted {path} to Editor-only.");
            updated++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"DatadogScaffoldPluginSettings: updated {updated} of {TestOnlyPluginPaths.Length} plugin importer(s).");
    }
}
