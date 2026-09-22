// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.IO;
using UnityEngine;

namespace Datadog.Unity.Editor.iOS
{
    /// <summary>
    /// Deserialized shape of IosDependencyVersion.json: the pinned dd-sdk-ios version, its
    /// XCFramework zip's SHA-256 digest, and the vendored module name list.
    /// </summary>
    [Serializable]
    internal class IosDependencyPinData
    {
        public string version;
        public string sha256;
        public string[] modules;
    }

    /// <summary>
    /// Resolves this package's install location and reads its pinned dd-sdk-ios
    /// XCFramework version/module list from IosDependencyVersion.json.
    /// </summary>
    internal static class IosDependencyVersion
    {
        internal const string PackageName = "com.datadoghq.unity";
        internal const string ConfigRelativePath = "Editor/iOS/IosDependencyVersion.json";
        internal const string PluginsIosRelativePath = "Plugins/iOS";

        internal static string ResolvePackageRoot()
        {
            return PackageRootResolver.Resolve(typeof(IosDependencyVersion).Assembly, nameof(IosDependencyVersion));
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
