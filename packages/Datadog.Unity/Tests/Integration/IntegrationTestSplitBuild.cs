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
        private static bool _originalAudioDisabled;

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions options)
        {
            if (!Application.isBatchMode || options.target != BuildTarget.iOS ||
                PlayerSettings.iOS.sdkVersion != iOSSdkVersion.SimulatorSDK)
            {
                return options;
            }

            // CoreAudio initialization can abort in virtualized iOS Simulators.
            _originalAudioDisabled = SetAudioDisabled(true);
            options.options &= ~(BuildOptions.AutoRunPlayer | BuildOptions.ConnectToHost);
            // Include the file result writer only in this exported player.
            options.extraScriptingDefines = (options.extraScriptingDefines ?? Array.Empty<string>())
                .Concat(new[] { "DATADOG_INTEGRATION_SPLIT" }).Distinct().ToArray();
            _exporting = true;
            Debug.Log($"Exporting integration test player without launching Xcode: {options.locationPathName}");
            return options;
        }

        private static bool SetAudioDisabled(bool disabled)
        {
            var audioManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0];
            using (var serializedManager = new SerializedObject(audioManager))
            {
                var property = serializedManager.FindProperty("m_DisableAudio");
                var original = property.boolValue;
                property.boolValue = disabled;
                serializedManager.ApplyModifiedProperties();
                return original;
            }
        }

        public void Cleanup()
        {
            if (!_exporting)
            {
                return;
            }
            // UTF skips restoring its temporary engine-stripping change when AutoRunPlayer is cleared.
            PlayerSettings.stripEngineCode = OriginalStripping;
            SetAudioDisabled(_originalAudioDisabled);
            _exporting = false;
            // Let every asset cleanup finish before closing the batch Editor.
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
    }
}
#endif
