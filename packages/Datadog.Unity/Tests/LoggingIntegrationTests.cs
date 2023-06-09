// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using Datadog.Unity;
using Datadog.Unity.Logs;
using Datadog.Unity.Tests.Integration;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests
{
    public class LoggingIntegrationTests
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator LoggingIntegrationScenario()
        {
            var mockServerHelper = new MockServerHelper();

            yield return new MonoBehaviourTest<TestLoggingMonoBehavior>();

            var task = mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (serverLog) =>
            {
                var logs = LogDecoder.LogsFromMockServer(serverLog);
                return logs.Count >= 3;
            });

            yield return new WaitUntil(() => task.IsCompleted);
            var serverLog = task.Result;
            var logs = LogDecoder.LogsFromMockServer(serverLog);

            Assert.AreEqual(3, logs.Count);

            var debugLog = logs[0];
            Assert.AreEqual("debug", debugLog.Status);
            Assert.AreEqual("debug message", debugLog.Message);
            Assert.AreEqual("logging.service", debugLog.ServiceName);
            Assert.Contains("tag1:tag-value", debugLog.Tags);
            Assert.Contains("my-tag", debugLog.Tags);
            Assert.AreEqual("not_silent_logger", debugLog.LoggerName);
            Assert.AreEqual("string", debugLog.RawJson["stringAttribute"]);

            var warnLog = logs[1];
            Assert.AreEqual("warn", warnLog.Status);
            Assert.AreEqual("warn message", warnLog.Message);
            Assert.AreEqual("logging.service", warnLog.ServiceName);
            Assert.Contains("tag1:tag-value", warnLog.Tags);
            Assert.Contains("my-tag", warnLog.Tags);
            var nestedAttribute = warnLog.RawJson["nestedAttribute"] as JObject;
            Assert.AreEqual("test", (string)nestedAttribute["internal"]);
            Assert.AreEqual(true, (bool)nestedAttribute["isValid"]);

            var infoLog = logs[2];
            Assert.AreEqual("info", infoLog.Status);
            Assert.AreEqual("Awake from test behavior", infoLog.Message);
            Assert.AreEqual("logging.service", infoLog.ServiceName);
            Assert.Contains("tag1:tag-value", infoLog.Tags);
            Assert.Contains("my-tag", infoLog.Tags);
            Assert.AreEqual("not_silent_logger", infoLog.LoggerName);
        }

        public class TestLoggingMonoBehavior : MonoBehaviour, IMonoBehaviourTest
        {
            private bool _didSendLog = false;

            public bool IsTestFinished
            {
                get { return _didSendLog; }
            }

            public void Awake()
            {
                DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);

                var silentLogger = DatadogSdk.Instance.CreateLogger(new()
                {
                    SendToDatadog = false,
                    LoggerName = "silent_logger",
                });
                silentLogger.Info("Interesting logging information");

                var loggingOptions = new DatadogLoggingOptions()
                {
                    ServiceName = "logging.service",
                    LoggerName = "not_silent_logger",
                };
                var logger = DatadogSdk.Instance.CreateLogger(loggingOptions);
                logger.AddTag("tag1", "tag-value");
                logger.AddTag("my-tag");

                logger.Debug("debug message", new() { {"stringAttribute", "string" } });

                logger.Warn("warn message", new() {
                    {
                        "nestedAttribute", new Dictionary<string, object>()
                        {
                            { "internal", "test" },
                            { "isValid", true },
                        }
                    },
                });

                logger.Info("Awake from test behavior");
                _didSendLog = true;
            }
        }
    }
}
