// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Editor
{
    public sealed class AndroidTestDeviceSetup : IPrebuildSetup
    {
        private const string StartedDevicesKey = "Datadog.IntegrationTests.StartedAndroidDevices";
        private const string ConnectedDevicesKey = "Datadog.IntegrationTests.ConnectedAndroidDevices";

        // Ports used by Unity's Android PlayerConnection bridge.
        private static readonly int[] EditorPorts =
        {
            55504, 55505, 55506, 55507, 55508, 55509, 55510, 55511, 34999,
        };

        public void Setup()
        {
            if (!Application.isBatchMode && EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                Start();
            }
        }

        internal static void Stop()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            var actions = new List<Action>();
            foreach (var port in EditorPorts)
            {
                var connectedSerials = SessionState.GetString($"{ConnectedDevicesKey}.{port}", string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var serial in connectedSerials)
                {
                    actions.Add(() => PythonScriptUtils.Run(
                        "dd-unity-start-simulator",
                        $"--platform android --action disconnect --serial \"{serial}\" --editor-port {port}",
                        environmentVariables: AndroidEnvironment()));
                }
            }

            var serials = SessionState.GetString(StartedDevicesKey, string.Empty)
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var serial in serials)
            {
                actions.Add(() => PythonScriptUtils.Run(
                    "dd-unity-start-simulator",
                    $"--platform android --action stop --serial \"{serial}\"",
                    environmentVariables: AndroidEnvironment()));
            }

            IntegrationTestSetup.RunAll(actions.ToArray());
            SessionState.EraseString(StartedDevicesKey);
            foreach (var port in EditorPorts)
            {
                SessionState.EraseString($"{ConnectedDevicesKey}.{port}");
            }
        }

        private static void Start()
        {
            PythonScriptUtils.Run(
                "dd-unity-start-simulator",
                "--platform android --action start",
                timeoutSeconds: 360,
                environmentVariables: AndroidEnvironment(),
                onOutput: RememberStartedDevice);

            // Unity may not discover emulators started during prebuild setup.
            // Mirror Unity discovery by ensuring the mapping on every connected device.
            // The helper returns only serials whose mappings it created for each port.
            foreach (var port in EditorPorts)
            {
                var connectedSerials = PythonScriptUtils.Run(
                    "dd-unity-start-simulator",
                    $"--platform android --action connect --editor-port {port}",
                    environmentVariables: AndroidEnvironment());
                if (!string.IsNullOrEmpty(connectedSerials))
                {
                    var key = $"{ConnectedDevicesKey}.{port}";
                    var connectedDevices = SessionState.GetString(key, string.Empty);
                    SessionState.SetString(key, connectedDevices + connectedSerials + "\n");
                }
            }
        }

        private static void RememberStartedDevice(string serial)
        {
            if (!string.IsNullOrEmpty(serial))
            {
                // Keep ownership even when startup fails or times out, and across assembly reloads.
                var startedDevices = SessionState.GetString(StartedDevicesKey, string.Empty);
                SessionState.SetString(StartedDevicesKey, startedDevices + serial + "\n");
            }
        }

        private static IReadOnlyDictionary<string, string> AndroidEnvironment()
        {
            // Avoid inheriting Unity's bundled Java for host SDK tools.
            return new Dictionary<string, string>
            {
                { "JAVA_HOME", string.Empty },
            };
        }
    }
}
