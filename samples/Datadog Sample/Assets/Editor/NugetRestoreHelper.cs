// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using NugetForUnity;
using UnityEditor;

namespace Datadog.Unity.Editor
{
    /// <summary>
    /// Provides a batchmode-callable entry point for NuGet package restoration.
    /// Called via: -executeMethod Datadog.Unity.Editor.NugetRestoreHelper.RestorePackages
    /// </summary>
    public static class NugetRestoreHelper
    {
        [MenuItem("Datadog/Restore NuGet Packages")]
        public static void RestorePackages()
        {
            NugetHelper.Restore();
        }
    }
}
