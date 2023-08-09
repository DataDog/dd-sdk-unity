// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
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
        public IEnumerator AutoLogginIntegrationScenario()
        {
            // Note -- For now the "Forward Unity Logs" flag needs to be set in the
            // projects settings for this to work (it is unset by default).
            LogAssert.ignoreFailingMessages = true;

            var mockServerHelper = new MockServerHelper();
            var resetTask = mockServerHelper.Clear();
            yield return new WaitUntil(() => resetTask.IsCompleted);

            yield return new MonoBehaviourTest<TestAutoLoggingMonoBehavior>();

            var task = mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (serverLog) =>
            {
                var logs = LogDecoder.LogsFromMockServer(serverLog);
                return logs.Count >= 4;
            });

            yield return new WaitUntil(() => task.IsCompleted);
            var serverLog = task.Result;
            var logs = LogDecoder.LogsFromMockServer(serverLog);

            Assert.AreEqual(4, logs.Count);

            // The first log is from Unity about `ignoreFailingMessages` being set and can be ignored
            // All other logs have the attribute set
            for (int i = 1; i < logs.Count; ++i)
            {
                var log = logs[i];
                Assert.AreEqual("attribute_value", (string)log.RawJson["attribute_1"]);
            }

            var infoLog = logs[1];
            Assert.AreEqual("info", infoLog.Status);
            Assert.AreEqual("Testing logging", infoLog.Message);

            var warnLog = logs[2];
            Assert.AreEqual("warn", warnLog.Status);
            Assert.AreEqual("Test warning", warnLog.Message);

            var exceptionLog = logs[3];
            Assert.AreEqual("critical", exceptionLog.Status);
            Assert.AreEqual("Error Message", exceptionLog.Message);
            Assert.AreEqual("Error Message", exceptionLog.ErrorMessage);
            Assert.AreEqual("System.InvalidOperationException", exceptionLog.ErrorKind);
            Assert.NotNull(exceptionLog.ErrorStack);
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
