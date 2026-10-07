// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using UnityEngine.TestTools;

#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Debug = UnityEngine.Debug;
#endif

namespace Datadog.Unity.Tests.Integration
{
    // IntegrationTestBase opts selected fixtures into this shared setup once per run.
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public class IntegrationTestEnvironment : IPrebuildSetup, IPostBuildCleanup
    {
#if UNITY_EDITOR
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
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.delayCall += () =>
            {
                if (!_settingsApplied && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Settings.Restore(); // Recover the asset after a previous Editor crash.
                }
            };
        }
#endif

        public void Setup()
        {
#if UNITY_EDITOR
            _settingsApplied = true;
            try
            {
                Settings.Restore();
                RunHelper("prepare");
                var server = JsonUtility.FromJson<ServerState>(File.ReadAllText(StatePath));
                Settings.Apply(server.endpoint);
            }
            catch
            {
                Settings.Restore();
                _settingsApplied = false;
                throw;
            }
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            // The player contains the test configuration now. It still needs the
            // server during execution, so post-build cleanup only restores settings.
            Settings.Restore();
            _settingsApplied = false;
#endif
        }

#if UNITY_EDITOR
        [MenuItem("Datadog/Tests/Start Mock Server")]
        public static void StartMockServer() => RunHelper("prepare");

        private static void OnEditorQuitting()
        {
            // Batch export exits before native execution; Python handles server cleanup.
            if (!Application.isBatchMode)
            {
                Finish();
            }
        }

        [MenuItem("Datadog/Tests/Stop Mock Server")]
        public static void Finish()
        {
            try
            {
                try
                {
                    Settings.Restore();
                }
                finally
                {
                    if (File.Exists(StatePath))
                    {
                        RunHelper("finish");
                    }
                }

                _settingsApplied = false;
            }
            catch (Exception error)
            {
                Debug.LogError($"Integration test cleanup failed: {error.Message}. Retry Datadog > Tests > Stop Mock Server.");
            }
        }

        [Serializable]
        private class ServerState
        {
            public string endpoint;
        }

        private static void RunHelper(string action)
        {
            var root = new DirectoryInfo(ProjectPath);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "tools/scripts/integration_test_setup.py")))
            {
                root = root.Parent;
            }

            if (root == null)
            {
                throw new InvalidOperationException("Integration tests require a project inside the dd-sdk-unity repository.");
            }

            var windows = Application.platform == RuntimePlatform.WindowsEditor;
            var script = Path.Combine(root.FullName, windows ? "run-script.bat" : "run-script");
            var arguments = $"integration_test_setup {action} --project \"{ProjectPath}\"";
            var startInfo = new ProcessStartInfo(
                windows ? "cmd.exe" : script,
                windows ? $"/d /s /c \"\"{script}\" {arguments}\"" : arguments)
            {
                WorkingDirectory = root.FullName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // GUI applications may not include the python.org installation on PATH.
                startInfo.EnvironmentVariables["PATH"] = "/usr/local/bin:" + startInfo.EnvironmentVariables["PATH"];
            }

            using (var process = Process.Start(startInfo))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(420000))
                {
                    process.Kill();
                    throw new TimeoutException("Integration test setup timed out. Check Python dependencies and GitHub access.");
                }

                var details = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Integration test {action} failed:\n{details}");
                }

                if (!string.IsNullOrWhiteSpace(details))
                {
                    Debug.Log(details);
                }
            }
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
#endif
    }
}
