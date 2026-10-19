// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration
{
    // Ensure the mock server is running, then apply and restore SDK settings.
    [InitializeOnLoad]
    public class IntegrationTestEnvironment : IPrebuildSetup, IPostBuildCleanup
    {
        private static bool settingsApplied;

        private static string ProjectPath => Directory.GetParent(Application.dataPath).FullName;

        private static string StatePath => Path.Combine(ProjectPath, "Library/DatadogIntegrationTests/state.json");

        private static IntegrationTestSettings Settings => new IntegrationTestSettings(
            DatadogConfigurationOptions.DefaultDatadogSettingsPath,
            Path.Combine(ProjectPath, "Library/DatadogIntegrationTests/DatadogSettings.original"));

        static IntegrationTestEnvironment()
        {
            EditorApplication.delayCall += () =>
            {
                if (!settingsApplied && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Settings.Restore(); // Recover the asset after a previous Editor crash.
                }
            };
        }

        [MenuItem("Datadog/Mock Server/Start")]
        public static void StartMockServer()
        {
            PythonScriptUtils.Run("dd-unity-integration-test-setup", $"start --project \"{ProjectPath}\"");
        }

        [MenuItem("Datadog/Mock Server/Inspect Requests")]
        public static void InspectMockServer()
        {
            var serverState = ReadServerState();
            if (serverState != null)
            {
                Application.OpenURL($"{serverState.Endpoint}/inspect/");
            }
        }

        [MenuItem("Datadog/Mock Server/Stop")]
        public static void StopMockServer()
        {
            PythonScriptUtils.Run("dd-unity-integration-test-setup", $"stop --project \"{ProjectPath}\"");
        }

        public void Setup()
        {
            settingsApplied = true;
            Settings.Restore();
            StartMockServer();
            var serverState = ReadServerState();
            if (serverState == null)
            {
                throw new InvalidOperationException("Cannot prepare integration tests without mock server state.");
            }

            Settings.Apply(serverState.Endpoint);
        }

        public void Cleanup()
        {
            // The player contains the test configuration now. It still needs the
            // server during execution, so post-build cleanup only restores settings.
            try
            {
                Settings.Restore();
            }
            finally
            {
                settingsApplied = false;
            }
        }

        private static ServerState ReadServerState()
        {
            try
            {
                return JsonConvert.DeserializeObject<ServerState>(File.ReadAllText(StatePath))
                       ?? throw new InvalidDataException("The mock server state is empty.");
            }
            catch (Exception exception)
            {
                if (Application.isBatchMode)
                {
                    throw;
                }

                EditorUtility.DisplayDialog(
                    "Datadog Mock Server",
                    $"Could not read the mock server state. Make sure it is running..\n\n{exception.Message}",
                    "OK");
                return null;
            }
        }

        private class ServerState
        {
            [JsonProperty("endpoint")]
            public string Endpoint { get; set; }
        }
    }
}
