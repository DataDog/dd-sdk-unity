// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Datadog.Unity.Editor
{
    /// <summary>
    /// Provides a batchmode-callable entry point for NuGet package restoration.
    /// Called via: -executeMethod Datadog.Unity.Editor.NugetRestoreHelper.RestorePackages
    /// Uses reflection to avoid a compile-time dependency on NuGetForUnity's internal types.
    /// </summary>
    public static class NugetRestoreHelper
    {
        [MenuItem("Datadog/Restore NuGet Packages")]
        public static void RestorePackages()
        {
            // NuGetForUnity's [InitializeOnLoad] already runs PackageRestorer.Restore() when
            // the editor loads; this entry point exists so CI can explicitly trigger a restore
            // via -executeMethod. We use reflection to avoid a compile-time assembly reference.
            var restorer = Type.GetType("NugetForUnity.PackageRestorer, NuGetForUnity");
            if (restorer == null)
            {
                Debug.LogWarning("NugetRestoreHelper: NugetForUnity.PackageRestorer not found. " +
                                 "Packages may already be restored by [InitializeOnLoad].");
                return;
            }

            var method = restorer.GetMethod("Restore", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                Debug.LogWarning("NugetRestoreHelper: PackageRestorer.Restore method not found.");
                return;
            }

            method.Invoke(null, new object[] { false });
        }
    }
}
