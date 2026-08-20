// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Datadog.Unity.Editor.iOS
{
    [Serializable]
    internal class IosDependencyPinData
    {
        public string version;
        public string sha256;
        public string[] modules;
    }

    internal static class IosDependencyVersion
    {
        internal const string PackageName = "com.datadoghq.unity";
        internal const string ConfigRelativePath = "Editor/iOS/IosDependencyVersion.json";
        internal const string PluginsIosRelativePath = "Plugins/iOS";

        internal static string ResolvePackageRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(IosDependencyVersion).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                return packageInfo.resolvedPath;
            }

            // Fallback for when the package is physically embedded under Assets/ rather than
            // resolved through the Package Manager (e.g. UPM's `file:` local-package mode may
            // still resolve above, but embedded/Assets-based installs need this path).
            var guids = AssetDatabase.FindAssets("IosDependencyVersion t:MonoScript");
            if (guids == null || guids.Length == 0)
            {
                return null;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            // assetPath is expected to end with .../Editor/iOS/IosDependencyVersion.cs; walk up
            // three levels (file -> iOS -> Editor -> package root).
            var editorIosDir = Path.GetDirectoryName(assetPath);
            var editorDir = editorIosDir != null ? Path.GetDirectoryName(editorIosDir) : null;
            var packageRoot = editorDir != null ? Path.GetDirectoryName(editorDir) : null;
            if (string.IsNullOrEmpty(packageRoot))
            {
                return null;
            }

            return Path.GetFullPath(packageRoot);
        }

        internal static string ResolvePluginsIosDirectory()
        {
            var packageRoot = ResolvePackageRoot();
            if (string.IsNullOrEmpty(packageRoot))
            {
                return null;
            }

            return Path.Combine(packageRoot, "Plugins", "iOS");
        }

        internal static IosDependencyPinData Parse(string json)
        {
            var data = JsonUtility.FromJson<IosDependencyPinData>(json);
            if (data == null || string.IsNullOrEmpty(data.version) || data.modules == null || data.modules.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Datadog: {ConfigRelativePath} is malformed or missing required fields (version, modules).");
            }

            return data;
        }

        internal static IosDependencyPinData Load()
        {
            var packageRoot = ResolvePackageRoot();
            if (string.IsNullOrEmpty(packageRoot))
            {
                throw new InvalidOperationException(
                    $"Datadog: could not resolve the {PackageName} package root to locate {ConfigRelativePath}.");
            }

            var configPath = Path.Combine(packageRoot, "Editor", "iOS", "IosDependencyVersion.json");
            if (!File.Exists(configPath))
            {
                throw new InvalidOperationException(
                    $"Datadog: missing iOS dependency version-pin config at {configPath}.");
            }

            var json = File.ReadAllText(configPath);
            return Parse(json);
        }
    }
}
