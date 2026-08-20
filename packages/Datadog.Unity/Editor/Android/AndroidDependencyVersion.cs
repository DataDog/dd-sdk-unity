// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.IO;
using UnityEngine;

namespace Datadog.Unity.Editor.Android
{
    [Serializable]
    internal class AndroidDependencyPinData
    {
        public string version;
        public string[] artifacts;
    }

    internal static class AndroidDependencyVersion
    {
        internal const string PackageName = "com.datadoghq.unity";
        internal const string ConfigRelativePath = "Editor/Android/AndroidDependencyVersion.json";
        internal const string MavenGroupId = "com.datadoghq";

        internal static string ResolvePackageRoot()
        {
            return PackageRootResolver.Resolve(typeof(AndroidDependencyVersion).Assembly, nameof(AndroidDependencyVersion));
        }

        internal static AndroidDependencyPinData Parse(string json)
        {
            var data = JsonUtility.FromJson<AndroidDependencyPinData>(json);
            if (data == null || string.IsNullOrEmpty(data.version) || data.artifacts == null || data.artifacts.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Datadog: {ConfigRelativePath} is malformed or missing required fields (version, artifacts).");
            }

            return data;
        }

        internal static AndroidDependencyPinData Load()
        {
            var packageRoot = ResolvePackageRoot();
            if (string.IsNullOrEmpty(packageRoot))
            {
                throw new InvalidOperationException(
                    $"Datadog: could not resolve the {PackageName} package root to locate {ConfigRelativePath}.");
            }

            var configPath = Path.Combine(packageRoot, "Editor", "Android", "AndroidDependencyVersion.json");
            if (!File.Exists(configPath))
            {
                throw new InvalidOperationException(
                    $"Datadog: missing Android dependency version-pin config at {configPath}.");
            }

            var json = File.ReadAllText(configPath);
            return Parse(json);
        }
    }
}
