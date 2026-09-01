// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Datadog.Unity.Editor.iOS
{
    /// <summary>
    /// Fails an iOS build early, with a clear message, if any of the pinned dd-sdk-ios
    /// XCFramework modules are missing from Plugins/iOS -- rather than letting the build
    /// proceed and fail deep inside Xcode's link step with a much less helpful error.
    /// </summary>
    public class IosXcframeworkPreprocessBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
            {
                return;
            }

            var pluginsIosDirectory = IosDependencyVersion.ResolvePluginsIosDirectory();
            var requiredModules = IosDependencyVersion.Load().modules;

            Validate(pluginsIosDirectory, requiredModules);
        }

        internal static List<string> FindMissingModules(string pluginsIosDirectory, IEnumerable<string> requiredModules)
        {
            var missing = new List<string>();
            foreach (var module in requiredModules)
            {
                if (string.IsNullOrEmpty(pluginsIosDirectory))
                {
                    missing.Add(module);
                    continue;
                }

                var modulePath = Path.Combine(pluginsIosDirectory, $"{module}.xcframework");
                if (!Directory.Exists(modulePath))
                {
                    missing.Add(module);
                }
            }

            return missing;
        }

        internal static void Validate(string pluginsIosDirectory, IEnumerable<string> requiredModules)
        {
            var missing = FindMissingModules(pluginsIosDirectory, requiredModules);
            if (missing.Count == 0)
            {
                return;
            }

            throw new BuildFailedException(
                $"Datadog: missing required XCFramework module(s) [{string.Join(", ", missing)}] under " +
                $"{pluginsIosDirectory}. In the development repo, run './run-script ios_xcframework stage' to " +
                "fetch and stage the pinned modules. A published package is expected to ship these modules " +
                "already vendored — see packages/Datadog.Unity/Editor/iOS/IosDependencyVersion.json for the " +
                "pinned dd-sdk-ios version.");
        }
    }
}
