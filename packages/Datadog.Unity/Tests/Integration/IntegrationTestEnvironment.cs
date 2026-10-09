// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration
{
    // The Python runner prepares the mock server; these hooks apply and restore SDK settings.
    [InitializeOnLoad]
    public class IntegrationTestEnvironment : IPrebuildSetup, IPostBuildCleanup
    {
        private static bool _settingsApplied;

        private static string ProjectPath => Directory.GetParent(Application.dataPath).FullName;

        private static string StatePath => Path.Combine(ProjectPath, "Library/DatadogIntegrationTests/state.json");

        private static IntegrationTestSettings Settings => new IntegrationTestSettings(
            DatadogConfigurationOptions.DefaultDatadogSettingsPath,
            Path.Combine(ProjectPath, "Library/DatadogIntegrationTests/DatadogSettings.original"));

        static IntegrationTestEnvironment()
        {
            // Register callbacks again after an assembly reload.
            TestRunnerApi.RegisterTestCallback(new RunCallbacks(), 100);
            EditorApplication.delayCall += () =>
            {
                if (!_settingsApplied && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Settings.Restore(); // Recover the asset after a previous Editor crash.
                }
            };
        }

        public void Setup()
        {
            _settingsApplied = true;
            try
            {
                Settings.Restore();
                var server = JsonUtility.FromJson<ServerState>(File.ReadAllText(StatePath));
                Settings.Apply(server.endpoint);
            }
            catch
            {
                Settings.Restore();
                _settingsApplied = false;
                throw;
            }
        }

        public void Cleanup()
        {
            // The player contains the test configuration now. It still needs the
            // server during execution, so post-build cleanup only restores settings.
            Settings.Restore();
            _settingsApplied = false;
        }

        [Serializable]
        private class ServerState
        {
            public string endpoint;
        }

        private class RunCallbacks : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result) => RestoreAfterRun();
            public void OnError(string message) => RestoreAfterRun();

            private static void RestoreAfterRun()
            {
                Settings.Restore();
                _settingsApplied = false;
            }
        }
    }
}
#endif
