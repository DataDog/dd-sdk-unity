// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using Datadog.Unity.Tests.Integration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Logging
{
    public class AutoLoggingIntegrationTests
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator AutoLoggingIntegrationScenario()
        {
            // Note -- For now the "Forward Unity Logs" flag needs to be set in the
            // projects settings for this to work (it is unset by default).
#if !DD_RUNTIME_INTEGRATION_TESTS
            LogAssert.ignoreFailingMessages = true;
#endif

            var mockServerHelper = new MockServerHelper();
            yield return mockServerHelper.Clear();

            yield return new MonoBehaviourTest<TestAutoLoggingMonoBehavior>();

            var timeoutTime = new TimeSpan(0, 0, 45);
            var logs = new List<LogDecoder>();
            yield return mockServerHelper.PollRequests(timeoutTime, (serverLog) =>
            {
                logs = LogDecoder.LogsFromMockServer(serverLog);
                return logs.Count >= 3;
            });

#if DD_RUNTIME_INTEGRATION_TESTS
            Assert.AreEqual(2, logs.Count);
#else
            Assert.AreEqual(3, logs.Count);
#endif

            // The first log is from Unity about `ignoreFailingMessages` being set and can be ignored
            // All other logs have the attribute set
            var infoLog = logs[logs.Count - 2];
            Assert.AreEqual("info", infoLog.Status);
            Assert.AreEqual("Testing logging", infoLog.Message);
            Assert.AreEqual("attribute_value", (string)infoLog.RawJson["attribute_1"]);

            var warnLog = logs[logs.Count - 1];
            Assert.AreEqual("warn", warnLog.Status);
            Assert.AreEqual("Test warning", warnLog.Message);
            Assert.AreEqual("attribute_value", (string)warnLog.RawJson["attribute_1"]);
        }

        public class TestAutoLoggingMonoBehavior : MonoBehaviour, IMonoBehaviourTest
        {
            private bool _didSendLog = false;

            public bool IsTestFinished
            {
                get { return _didSendLog; }
            }

            public void Awake()
            {
                DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);

                DatadogSdk.Instance.DefaultLogger.AddAttribute("attribute_1", "attribute_value");

                Debug.Log("Testing logging");
                Debug.LogWarning("Test warning");
            }

            public void Update()
            {
                if (!_didSendLog)
                {
                    _didSendLog = true;
                    throw new InvalidOperationException("Error Message");
                }
            }
        }
    }
}
