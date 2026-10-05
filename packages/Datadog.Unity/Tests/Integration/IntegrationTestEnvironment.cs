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
        private const string SessionKey = "Datadog.IntegrationTestEnvironment";

        private static string ProjectPath => Directory.GetParent(Application.dataPath).FullName;

        private static string StatePath => Path.Combine(ProjectPath, "Library/DatadogIntegrationTests/state.json");

        private static IntegrationTestSettings Settings => new IntegrationTestSettings(
            DatadogConfigurationOptions.DefaultDatadogSettingsPath,
            Path.Combine(ProjectPath, "Library/DatadogIntegrationTests/DatadogSettings.original"));

        static IntegrationTestEnvironment()
        {
            // Callbacks are not retained across domain reloads; SessionState is.
            TestRunnerApi.RegisterTestCallback(new RunCallbacks(), 100);
            EditorApplication.quitting += Finish;
            EditorApplication.delayCall += () =>
            {
                if (!SessionState.GetBool(SessionKey, false))
                {
                    Finish(); // Recover a previous Editor crash.
                }
            };
        }
#endif

        public void Setup()
        {
#if UNITY_EDITOR
            SessionState.SetBool(SessionKey, true);
            try
            {
                Settings.Restore();
                RunHelper("prepare");
                var server = JsonUtility.FromJson<ServerState>(File.ReadAllText(StatePath));
                Settings.Apply(server.endpoint);
            }
            catch
            {
                Finish();
                throw;
            }
#endif
        }

        public void Cleanup()
        {
#if UNITY_EDITOR
            // The player contains the test configuration now. It still needs the
            // server until RunFinished, so post-build cleanup only restores settings.
            Settings.Restore();
#endif
        }

#if UNITY_EDITOR
        [MenuItem("Datadog/Tests/Stop Integration Test Environment")]
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

                SessionState.SetBool(SessionKey, false);
            }
            catch (Exception error)
            {
                Debug.LogError($"Integration test cleanup failed: {error.Message}. Retry Datadog > Tests > Stop Integration Test Environment.");
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

            var python = Path.Combine(root.FullName, "tools/scripts/venv",
                Application.platform == RuntimePlatform.WindowsEditor ? "Scripts/python.exe" : "bin/python");
            if (!File.Exists(python))
            {
                // GUI applications may not inherit the terminal's PATH. Prefer the
                // python.org installation on macOS when no repository venv exists.
                python = Application.platform == RuntimePlatform.OSXEditor && File.Exists("/usr/local/bin/python3")
                    ? "/usr/local/bin/python3" : "python3";
            }

            var script = Path.Combine(root.FullName, "tools/scripts/integration_test_setup.py");
            var startInfo = new ProcessStartInfo(python,
                $"\"{script}\" {action} --project \"{ProjectPath}\"")
            {
                WorkingDirectory = root.FullName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

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
            public void RunFinished(ITestResultAdaptor result) => Finish();
            public void OnError(string message) => Finish();
        }
#endif
    }
}
