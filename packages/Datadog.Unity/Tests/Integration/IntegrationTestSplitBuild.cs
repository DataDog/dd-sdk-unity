// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;
using Datadog.Unity.Tests.Integration;

[assembly: TestPlayerBuildModifier(typeof(IntegrationTestSplitBuild))]
[assembly: PostBuildCleanup(typeof(IntegrationTestSplitBuild))]

namespace Datadog.Unity.Tests.Integration
{
    [InitializeOnLoad]
    public sealed class IntegrationTestSplitBuild : ITestPlayerBuildModifier, IPostBuildCleanup
    {
        // UTF temporarily disables removal of unused engine code for test players.
        private static readonly bool OriginalStripping = PlayerSettings.stripEngineCode;
        private static bool _exporting;

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions options)
        {
            if (!Application.isBatchMode || options.target != BuildTarget.iOS ||
                PlayerSettings.iOS.sdkVersion != iOSSdkVersion.SimulatorSDK)
            {
                return options;
            }

            options.options &= ~(BuildOptions.AutoRunPlayer | BuildOptions.ConnectToHost);
            // Include the file result writer only in this exported player.
            options.extraScriptingDefines = (options.extraScriptingDefines ?? Array.Empty<string>())
                .Concat(new[] { "DATADOG_INTEGRATION_SPLIT" }).Distinct().ToArray();
            _exporting = true;
            Debug.Log($"Exporting integration test player without launching Xcode: {options.locationPathName}");
            return options;
        }

        public void Cleanup()
        {
            if (!_exporting)
            {
                return;
            }
            // UTF skips restoring its temporary engine-stripping change when AutoRunPlayer is cleared.
            PlayerSettings.stripEngineCode = OriginalStripping;
            _exporting = false;
            // Let every asset cleanup finish before closing the batch Editor.
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
#endif
