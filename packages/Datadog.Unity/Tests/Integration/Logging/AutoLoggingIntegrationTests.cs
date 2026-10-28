// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Tests.Integration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Logging
{
    public class AutoLoggingIntegrationTests : IntegrationTestBase
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator AutoLoggingIntegrationScenario()
        {
            // Note -- For now the "Forward Unity Logs" flag needs to be set in the
            // projects settings for this to work (it is unset by default).
            LogAssert.ignoreFailingMessages = true;

            var mockServerHelper = new MockServerHelper();
            yield return mockServerHelper.Clear();

            yield return new MonoBehaviourTest<TestAutoLoggingMonoBehavior>();

            var timeoutTime = new TimeSpan(0, 0, 45);
            var logs = new List<LogDecoder>();
            yield return mockServerHelper.PollRequests(timeoutTime, (serverLog) =>
            {
                // The Browser SDK can also send automatic logs from earlier scenarios.
                logs = LogDecoder.LogsFromMockServer(serverLog)
                    .Where(log => log.Message == "Testing logging" || log.Message == "Test warning")
                    .ToList();
                return logs.Count >= 2;
            });

            Assert.AreEqual(2, logs.Count);

            foreach (var log in logs)
            {
                Assert.AreEqual("attribute_value", (string)log.RawJson["attribute_1"]);
            }

            var infoLog = logs[0];
            Assert.AreEqual("info", infoLog.Status);
            Assert.AreEqual("Testing logging", infoLog.Message);

            var warnLog = logs[1];
            Assert.AreEqual("warn", warnLog.Status);
            Assert.AreEqual("Test warning", warnLog.Message);
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
