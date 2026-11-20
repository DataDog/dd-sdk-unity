// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Datadog.Unity.Editor.iOS
{
    /// <summary>
    /// Validates the consuming project's iOS deployment target and required XCFramework modules
    /// before building, and enables embedding for the native frameworks.
    /// </summary>
    public class IosXcframeworkPreprocessBuild : IPreprocessBuildWithReport
    {
        // Keep in sync with the minimum deployment target of the pinned dd-sdk-ios frameworks.
        private static readonly Version MinimumIosVersion = new Version(15, 0);

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            ValidateDeploymentTarget(PlayerSettings.iOS.targetOSVersionString);

            var artifact = IosDependencyVersion.Load().artifacts.ios;
            var pluginsIosDirectory = IosDependencyVersion.ResolvePluginsIosDirectory(artifact);
            var requiredFiles = artifact.files.Keys.Where(file => file.EndsWith(".xcframework", StringComparison.Ordinal));

            Validate(pluginsIosDirectory, requiredFiles);

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(IosDependencyVersion).Assembly);
            var pluginsAssetPath = packageInfo != null
                ? $"{packageInfo.assetPath}/{artifact.destination}"
                : FileUtil.GetProjectRelativePath(pluginsIosDirectory).Replace('\\', '/');

            foreach (var file in requiredFiles)
            {
                var assetPath = $"{pluginsAssetPath}/{file}";
                var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
                if (importer == null)
                {
                    throw new BuildFailedException($"Datadog: no native plugin importer found for {assetPath}.");
                }

                // Linking lets Xcode find the framework while building. Embedding
                // also makes it available when the app runs without Xcode's debugger.
                if (importer.GetPlatformData(BuildTarget.iOS, "AddToEmbeddedBinaries") != "true")
                {
                    importer.SetPlatformData(BuildTarget.iOS, "AddToEmbeddedBinaries", "true");
                    importer.SaveAndReimport();
                }
            }
        }

        internal static void ValidateDeploymentTarget(string targetVersion)
        {
            var versionString = targetVersion != null && !targetVersion.Contains(".")
                ? $"{targetVersion}.0"
                : targetVersion;
            if (Version.TryParse(versionString, out var version) && version >= MinimumIosVersion)
            {
                return;
            }

            throw new BuildFailedException(
                $"Datadog requires iOS {MinimumIosVersion} or later. " +
                "Set Player Settings > Other Settings > Target minimum iOS Version to " +
                $"{MinimumIosVersion} or later (currently '{targetVersion}').");
        }

        internal static List<string> FindMissingFiles(string pluginsIosDirectory, IEnumerable<string> requiredFiles)
        {
            var missing = new List<string>();
            foreach (var file in requiredFiles)
            {
                if (string.IsNullOrEmpty(pluginsIosDirectory))
                {
                    missing.Add(file);
                    continue;
                }

                var pluginPath = Path.Combine(pluginsIosDirectory, file);
                if (!Directory.Exists(pluginPath))
                {
                    missing.Add(file);
                }
            }

            return missing;
        }

        internal static void Validate(string pluginsIosDirectory, IEnumerable<string> requiredFiles)
        {
            var missing = FindMissingFiles(pluginsIosDirectory, requiredFiles);
            if (missing.Count == 0)
            {
                return;
            }

            throw new BuildFailedException(
                $"Datadog: missing required XCFramework(s) [{string.Join(", ", missing)}] under " +
                $"{pluginsIosDirectory}. In the development repo, run 'dd-unity-manage-sdk-dependencies stage' to " +
                "fetch and stage the pinned files. A published package is expected to ship these files " +
                "already vendored — see packages/Datadog.Unity/Editor/iOS/IosDependencyVersion.json for the " +
                "pinned dd-sdk-ios version.");
        }
    }
}
