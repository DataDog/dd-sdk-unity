// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Tests.Integration;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Editor
{
    [InitializeOnLoad]
    public sealed class IntegrationTestSetup : IPrebuildSetup, IPostBuildCleanup
    {
        static IntegrationTestSetup()
        {
            TestRunnerApi.RegisterTestCallback(new RunCallbacks(), 100);
            if (!Application.isBatchMode)
            {
                // The CLI runner owns the server and device lifecycles in batch mode.
                EditorApplication.quitting += () => RunAll(
                    () => new IntegrationTestEnvironment().Cleanup(),
                    () => AndroidTestDeviceSetup.Stop(),
                    () => IntegrationTestEnvironment.StopMockServer());
            }
        }

        public void Setup()
        {
            var exceptions = CollectErrors(
                () => new AndroidTestDeviceSetup().Setup(),
                () => new IntegrationTestEnvironment().Setup());
            if (exceptions.Count > 0)
            {
                exceptions.AddRange(CollectErrors(
                    () => new IntegrationTestEnvironment().Cleanup(),
                    () => AndroidTestDeviceSetup.Stop()));
                throw new AggregateException("Integration test setup failed.", exceptions);
            }
        }

        public void Cleanup()
        {
            RunAll(() => new IntegrationTestEnvironment().Cleanup());
        }

        internal static void RunAll(params Action[] actions)
        {
            var exceptions = CollectErrors(actions);
            if (exceptions.Count > 0)
            {
                throw new AggregateException("Integration test cleanup failed.", exceptions);
            }
        }

        private static List<Exception> CollectErrors(params Action[] actions)
        {
            var exceptions = new List<Exception>();
            foreach (var action in actions)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    exceptions.Add(exception);
                }
            }

            return exceptions;
        }

        private class RunCallbacks : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (result.FailCount > 0)
                {
                    WarnAboutAdbPreference();
                }

                Cleanup();
            }

            public void OnError(string message)
            {
                WarnAboutAdbPreference();
                Cleanup();
            }

            private static void WarnAboutAdbPreference()
            {
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android &&
                    EditorPrefs.GetBool("AndroidADBKillExternalInstance", true))
                {
                    Debug.LogWarning(
                        "If Android tests lose connection to the player or mock server, disable " +
                        "'Kill external ADB instances' in Unity Preferences > External Tools. " +
                        "Unity can otherwise terminate the ADB server used by Rider or other Android tools. " +
                        "This preference applies to your user across projects on this machine.");
                }
            }

            private static void Cleanup()
            {
                RunAll(
                    () => new IntegrationTestEnvironment().Cleanup(),
                    () => AndroidTestDeviceSetup.Stop());
            }
        }
    }
}
