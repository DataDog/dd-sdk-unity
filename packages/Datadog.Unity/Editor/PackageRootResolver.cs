// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;

namespace Datadog.Unity.Editor
{
    internal static class PackageRootResolver
    {
        // Resolves the root directory of the package containing the calling script. Tries the
        // Package Manager first (covers registry, git, and file: local-package installs); falls
        // back to locating the script asset itself and walking up from it, which is needed when
        // the package is physically embedded under Assets/ with no manifest.json entry at all.
        internal static string Resolve(Assembly callingAssembly, string scriptTypeName)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(callingAssembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                return packageInfo.resolvedPath;
            }

            var guids = AssetDatabase.FindAssets($"{scriptTypeName} t:MonoScript");
            if (guids == null || guids.Length == 0)
            {
                return null;
            }

            var assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            // assetPath is expected to end with .../Editor/<Platform>/<scriptTypeName>.cs; walk up
            // three levels (file -> platform dir -> Editor -> package root).
            var platformDir = Path.GetDirectoryName(assetPath);
            var editorDir = platformDir != null ? Path.GetDirectoryName(platformDir) : null;
            var packageRoot = editorDir != null ? Path.GetDirectoryName(editorDir) : null;
            if (string.IsNullOrEmpty(packageRoot))
            {
                return null;
            }

            return Path.GetFullPath(packageRoot);
        }
    }
}
