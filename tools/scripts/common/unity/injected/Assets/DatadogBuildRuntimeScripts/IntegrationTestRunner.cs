// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Datadog.Unity.RuntimeTest
{
    using IntegrationTestType = Tuple<Type, List<MethodInfo>>;

    public class IntegrationTestRunner : MonoBehaviour
    {
        public const string IntegrationTestNamespace = "Datadog.Unity.Tests.Integration";
        public const string IntegrationTestCategory = "integration";

        private bool _hasFailedTests = false;
        private bool _isTestRunning = false;
        private bool _currentTestIgnoresFailingLogMessages = false;
        private string _currentTestErrorMessage = null;
        private string _currentTestStackTrace = null;

        private void Awake()
        {
            // Don't permit more than one instance of IntegrationTestRunner, e.g. if we navigate
            // back to our integration test scene during tests, don't load a second runner
            if (FindObjectsOfType<IntegrationTestRunner>().Length > 1)
            {
                Destroy(gameObject);
                return;
            }

            // We're the one and only IntegrationTestRunner; ensure we persist across scene changes
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Register a log callback so we can detect exceptions during async test execution
            Application.logMessageReceived += OnLogMessageReceived;

            // Resolve all integration test types, failing if we have none loaded
            var types = FindIntegrationTestTypes();
            if (types.Count == 0)
            {
                IntegrationTestLog.Error("No tests found");
                StartCoroutine(DelayedExit(1));
                return;
            }

            // Write machine-parseable output describing the tests we plan to run
            IntegrationTestLog.Announce(types);

            // Start running the tests serially without blocking the main thread
            StartCoroutine(RunTestsSequentially(types));
        }

        private IEnumerator DelayedExit(int code)
        {
            yield return new WaitForSeconds(1.5f);
            Application.Quit(code);
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
        }

        private void OnLogMessageReceived(string logString, string stackTrace, LogType type)
        {
            // When we call StartCoroutine to run a test, Unity takes over execution and will not
            // propagate exceptions up to us. If Unity catches an exception, it will log it and
            // immediately terminate the coroutine, so we need to monitor the log for exceptions
            // while tests are running.
            if (_isTestRunning && type == LogType.Exception)
            {
                // Only capture the first exception logged
                if (_currentTestErrorMessage == null)
                {
                    if (!_currentTestIgnoresFailingLogMessages || logString != "InvalidOperationException: Error Message")
                    {
                        _currentTestErrorMessage = logString;
                        _currentTestStackTrace = stackTrace;
                    }
                }
            }
        }

        private static List<IntegrationTestType> FindIntegrationTestTypes()
        {
            // Inspect all loaded assemblies to find all types that contain integration tests
            var integrationTestTypes = new List<IntegrationTestType>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Resolve a list of types defined in this assembly
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip any assemblies that can't be loaded
                    continue;
                }

                // Examine all types defined in this assembly
                foreach (var type in types)
                {
                    // Skip any types not declared in the integration test namespace
                    if (type.Namespace == null || !type.Namespace.StartsWith(IntegrationTestNamespace))
                    {
                        continue;
                    }

                    // Find all methods on this type that represent Unity integration tests
                    var integrationTestMethods = new List<MethodInfo>();
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        // Require a [UnityTest] attribute
                        if (!method.GetCustomAttributes().Any(attr => attr.GetType().Name == "UnityTestAttribute"))
                        {
                            continue;
                        }

                        // Require [Category("integration")]
                        var categoryAttr = method.GetCustomAttributes().FirstOrDefault(attr => attr.GetType().Name == "CategoryAttribute");
                        if (categoryAttr == null)
                        {
                            continue;
                        }
                        var categoryNameProp = categoryAttr.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                        if (categoryNameProp == null)
                        {
                            continue;
                        }
                        var categoryNameValue = categoryNameProp.GetValue(categoryAttr)?.ToString();
                        if (categoryNameValue != IntegrationTestCategory)
                        {
                            continue;
                        }

                        // Check for [UnityPlatform], and ignore this test if it's not enabled for
                        // our current platform
                        var platformAttr = method.GetCustomAttributes().FirstOrDefault(attr => attr.GetType().Name == "UnityPlatformAttribute");
                        if (platformAttr != null)
                        {
                            // If current platform is explicitly excluded, skip this test
                            var excludeProp = platformAttr.GetType().GetProperty("exclude", BindingFlags.Public | BindingFlags.Instance);
                            var excludeArray = excludeProp?.GetValue(platformAttr) as Array;
                            if (excludeArray != null)
                            {
                                if (Array.IndexOf(excludeArray, Application.platform) >= 0)
                                {
                                    continue;
                                }
                            }

                            // If the attribute specifies an explicit list of included platforms,
                            // and that list does not include our current platform, skip the test
                            var includeProp = platformAttr.GetType().GetProperty("include", BindingFlags.Public | BindingFlags.Instance);
                            var includeArray = includeProp?.GetValue(platformAttr) as Array;
                            if (includeArray != null && includeArray.Length > 0)
                            {
                                if (Array.IndexOf(includeArray, Application.platform) < 0)
                                {
                                    continue;
                                }
                            }
                        }

                        // This method is the entry point of an integration test
                        integrationTestMethods.Add(method);
                    }

                    // If this type had any integration test methods, add it to our list
                    if (integrationTestMethods.Count > 0)
                    {
                        integrationTestTypes.Add(Tuple.Create(type, integrationTestMethods));
                    }
                }
            }
            return integrationTestTypes;
        }

        private IEnumerator RunTestsSequentially(List<IntegrationTestType> types)
        {
            // Run each test method one-by-one, setting the _hasFailedTests flag on any failure
            for (var typeIndex = 0; typeIndex < types.Count; typeIndex++)
            {
                var type = types[typeIndex].Item1;
                var methods = types[typeIndex].Item2;

                for (var methodIndex = 0; methodIndex < methods.Count; methodIndex++)
                {
                    var method = methods[methodIndex];
                    yield return StartCoroutine(RunSingleTest(type, method, typeIndex, methodIndex));
                }
            }

            // Once we're finished, exit with status code 0 to indicate that all tests passed, or
            // 2 to indicate that one or more tests failed (exit code 1 indicates test setup error)
            var ok = !_hasFailedTests;
            IntegrationTestLog.Exit(ok);
            StartCoroutine(DelayedExit(ok ? 0 : 2));
        }

        private IEnumerator RunSingleTest(Type testType, MethodInfo testMethod, int typeIndex, int methodIndex)
        {
            // Start the clock on timeouts and test duration stats
            var testStartTime = DateTime.Now;
            IntegrationTestLog.Invoke(typeIndex, methodIndex, $"{testType.FullName}.{testMethod.Name}");

            // Reset state for this test invocation
            _isTestRunning = true;
            _currentTestIgnoresFailingLogMessages = false;
            _currentTestErrorMessage = null;
            _currentTestStackTrace = null;

            // TODO: This is a simple hack to make existing integration tests work
            if (testType.FullName == "Datadog.Unity.Tests.Integration.Logging.AutoLoggingIntegrationTests")
            {
                if (testMethod.Name == "AutoLoggingIntegrationScenario")
                {
                    _currentTestIgnoresFailingLogMessages = true;
                }
            }

            // Create an instance of our test type and invoke the test method
            object testInstance = null;
            object result = null;
            Exception immediateException = null;
            try
            {
                testInstance = Activator.CreateInstance(testType);
                result = testMethod.Invoke(testInstance, null);
            }
            catch (Exception ex)
            {
                // If instance creation failed, or if the test method threw an immediate exception,
                // log a test failure and abort
                _hasFailedTests = true;
                _isTestRunning = false;

                var duration = DateTime.Now - testStartTime;
                var errorMessage = ex.ToString();
                var stackTrace = ex.InnerException?.StackTrace ?? ex.StackTrace;
                IntegrationTestLog.ResultFailed(typeIndex, methodIndex, duration, errorMessage, stackTrace);
                yield break;
            }

            // Unity tests should return an IEnumerator: block until that coroutine is done,
            // checking for exceptions and reporting test result
            if (result is IEnumerator coroutine)
            {
                yield return StartCoroutine(ExecuteTestCoroutineWithMonitoring(coroutine, testStartTime, typeIndex, methodIndex));
            }
            else
            {
                _hasFailedTests = true;
                var duration = DateTime.Now - testStartTime;
                IntegrationTestLog.ResultFailed(typeIndex, methodIndex, duration, "ERROR: Result of test invocation is not IEnumerator", "");
            }
        }

        private IEnumerator ExecuteTestCoroutineWithMonitoring(IEnumerator testCoroutine, DateTime testStartTime, int typeIndex, int methodIndex)
        {
            // Wrap the test coroutine so we'll know when it's finished
            bool coroutineFinished = false;
            IEnumerator WrapperCoroutine()
            {
                yield return testCoroutine;
                coroutineFinished = true;
            }

            // If Unity catches an exception, it will immediately terminate the coroutine: rather
            // than `yield return`, which would hang in the event of a test failure, we keep a
            // reference to the coroutine so we can poll it
            Coroutine runningCoroutine = StartCoroutine(WrapperCoroutine());

            // Establish a timeout in case the test hangs
            const double timeoutSeconds = 60.0;
            var timeoutDeadline = testStartTime.AddSeconds(timeoutSeconds);
            bool timeoutExceeded = false;

            // Block until the coroutine either finished or times out
            while (runningCoroutine != null && !coroutineFinished)
            {
                // If we caught an exception, give it a moment to see if the coroutine terminates
                // then break out to handle the failure
                if (_currentTestErrorMessage != null)
                {
                    yield return new WaitForSeconds(0.1f);
                    break;
                }

                // If the coroutine is still running but we've exceeded our timeout, abort
                if (DateTime.Now > timeoutDeadline)
                {
                    timeoutExceeded = true;
                    break;
                }

                // Keep spinning
                yield return new WaitForSeconds(0.1f);
            }

            // If we've timed out (or otherwise broken out of the loop) and our coroutine is still
            // running, explicitly terminate it
            if (runningCoroutine != null && !coroutineFinished)
            {
                StopCoroutine(runningCoroutine);
            }

            // The current test has ended: report a failure if we saw an exception reported to the
            // Unity log during test execution; report a failure if we timed out; or report success
            // otherwise
            _isTestRunning = false;
            var duration = DateTime.Now - testStartTime;
            if (_currentTestErrorMessage != null)
            {
                _hasFailedTests = true;
                IntegrationTestLog.ResultFailed(typeIndex, methodIndex, duration, _currentTestErrorMessage, _currentTestStackTrace ?? "");
            }
            else if (timeoutExceeded)
            {
                _hasFailedTests = true;
                IntegrationTestLog.ResultFailed(typeIndex, methodIndex, duration, $"ERROR: Test timed out after {timeoutSeconds:F0} seconds", "");
            }
            else
            {
                IntegrationTestLog.ResultPassed(typeIndex, methodIndex, duration);
            }
        }
    }

    public static class IntegrationTestLog
    {
        public static void Announce(List<IntegrationTestType> types)
        {
            Write("ANNOUNCE", $"Found {types.Count} integration test types");
            for (var typeIndex = 0; typeIndex < types.Count; typeIndex++)
            {
                var type = types[typeIndex].Item1;
                var methods = types[typeIndex].Item2;
                Write($"ANNOUNCE:{typeIndex}", type.FullName);
                for (var methodIndex = 0; methodIndex < methods.Count; methodIndex++)
                {
                    var method = methods[methodIndex];
                    Write($"ANNOUNCE:{typeIndex}:{methodIndex}", method.Name);
                }
            }
        }

        public static void Invoke(int typeIndex, int methodIndex, string fullyQualifiedTestName)
        {
            Write($"INVOKE:{typeIndex}:{methodIndex}", $"{fullyQualifiedTestName} started");
        }

        public static void ResultPassed(int typeIndex, int methodIndex, TimeSpan duration)
        {
            Write($"RESULT:{typeIndex}:{methodIndex}", $"PASSED after {duration.TotalSeconds:F2}s");
        }

        public static void ResultFailed(int typeIndex, int methodIndex, TimeSpan duration, string errorMessage, string stackTrace)
        {
            var prefix = $"RESULT:{typeIndex}:{methodIndex}";
            Write(prefix, $"FAILED after {duration.TotalSeconds:F2}s");
            WriteMultiline($"{prefix}:ERROR", errorMessage);
            if (!string.IsNullOrEmpty(stackTrace))
            {
                WriteMultiline($"{prefix}:STACK", stackTrace);
            }
        }

        public static void Exit(bool ok)
        {
            if (ok)
            {
                Write("EXIT", "OK");
            }
            else
            {
                Write("EXIT", "Failed");
            }
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        private static void Write(string prefix, string message)
        {
#if UNITY_IOS && !UNITY_EDITOR
            // Unity does not reliably route Debug.Log output to syslog on iOS simulator builds; so
            // when building integration tests for iOS we also inject a wrapper for os_log() in
            // Assets/Plugins/iOS/IntegrationTestLogger.m
            IOSLogger.Log($":: IntegrationTestRunner [{prefix}] {message}");
#else
            // We must use IInternalLogger.DatadogTag to ensure that integration test status
            // message aren't sent to intake (see DdUnityLogHandler.cs)
            Debug.unityLogger.Log("Datadog", $":: IntegrationTestRunner [{prefix}] {message}");
#endif
        }

        private static void WriteMultiline(string prefix, string message)
        {
            using var reader = new StringReader(message);
            string line;
            int lineIndex = 0;
            while ((line = reader.ReadLine()) != null)
            {
                Write($"{prefix}:{lineIndex++}", line);
            }
        }
    }

#if UNITY_IOS && !UNITY_EDITOR
    public static class IOSLogger
    {
        [DllImport("__Internal")]
        private static extern void LogToUnifiedSystem(string msg);

        public static void Log(string msg)
        {
            LogToUnifiedSystem(msg);
        }
    }
#endif
}
