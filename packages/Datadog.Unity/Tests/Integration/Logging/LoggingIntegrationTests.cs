// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using Datadog.Unity.Logs;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Logging
{
    public class LoggingIntegrationTests
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator LoggingIntegrationScenario()
        {
            var mockServerHelper = new MockServerHelper();
            var resetTask = mockServerHelper.Clear();
            yield return new WaitUntil(() => resetTask.IsCompleted);

            yield return new MonoBehaviourTest<TestLoggingMonoBehavior>();

            var task = mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (serverLog) =>
            {
                var logs = LogDecoder.LogsFromMockServer(serverLog);
                return logs.Count >= 5;
            });

            yield return new WaitUntil(() => task.IsCompleted);
            var serverLog = task.Result;
            var logs = LogDecoder.LogsFromMockServer(serverLog);

            foreach (var log in serverLog)
            {
                foreach (var request in log.Requests)
                {
                    Assert.AreEqual("unity", request.QueryParameters["ddsource"]);
                }
            }

            Assert.AreEqual(5, logs.Count);

            var debugLog = logs[0];
            Assert.AreEqual("debug", debugLog.Status);
            Assert.AreEqual("debug message", debugLog.Message);
            Assert.AreEqual("logging.service", debugLog.ServiceName);
            Assert.AreEqual("string value", (string)debugLog.RawJson["logger-attribute1"]);
            Assert.AreEqual(1000, (long)debugLog.RawJson["logger-attribute2"]);
            Assert.IsFalse(debugLog.RawJson.ContainsKey("global-attribute-1"));
            Assert.IsFalse(debugLog.RawJson.ContainsKey("global-attribute-2"));
            Assert.Contains("tag1:tag-value", debugLog.Tags);
            Assert.Contains("tag1:second-value", debugLog.Tags);
            Assert.Contains("my-tag", debugLog.Tags);
            Assert.AreEqual("not_silent_logger", debugLog.LoggerName);
            Assert.AreEqual("string", debugLog.RawJson["stringAttribute"]);

            var infoLog = logs[1];
            Assert.AreEqual("info", infoLog.Status);
            Assert.AreEqual("info message", infoLog.Message);
            Assert.AreEqual("logging.service", infoLog.ServiceName);
            Assert.Contains("tag1:tag-value", infoLog.Tags);
            Assert.Contains("tag1:second-value", debugLog.Tags);
            CollectionAssert.DoesNotContain(infoLog.Tags, "my-tag");
            Assert.AreEqual("string value", (string)infoLog.RawJson["logger-attribute1"]);
            Assert.AreEqual("value-1", (string)infoLog.RawJson["global-attribute-1"]);
            Assert.AreEqual(1255, (long)infoLog.RawJson["global-attribute-2"]);
            Assert.AreEqual(1000, (long)infoLog.RawJson["logger-attribute2"]);
            var nestedAttribute = infoLog.RawJson["nestedAttribute"] as JObject;
            Assert.AreEqual("test", (string)nestedAttribute["internal"]);
            Assert.AreEqual(true, (bool)nestedAttribute["isValid"]);

            var warnLog = logs[2];
            Assert.AreEqual("warn", warnLog.Status);
            Assert.AreEqual("warn message", warnLog.Message);
            Assert.AreEqual("logging.service", warnLog.ServiceName);
            Assert.Contains("tag1:tag-value", warnLog.Tags);
            Assert.Contains("tag1:second-value", debugLog.Tags);
            CollectionAssert.DoesNotContain(warnLog.Tags, "my-tag");
            Assert.AreEqual("string value", (string)warnLog.RawJson["logger-attribute1"]);
            Assert.AreEqual("value-1", (string)infoLog.RawJson["global-attribute-1"]);
            Assert.AreEqual(1255, (long)infoLog.RawJson["global-attribute-2"]);
            Assert.AreEqual(1000, (long)warnLog.RawJson["logger-attribute2"]);
            Assert.AreEqual(10.34, (double)warnLog.RawJson["doubleAttribute"]);

            var errorLog = logs[3];
            Assert.AreEqual("error", errorLog.Status);
            Assert.AreEqual("error message", errorLog.Message);
            Assert.AreEqual("logging.service", errorLog.ServiceName);
            CollectionAssert.DoesNotContain(errorLog.Tags, "tag1:tag-value");
            CollectionAssert.DoesNotContain(errorLog.Tags, "tag1:second-value");
            CollectionAssert.DoesNotContain(errorLog.Tags, "my-tag");
            Assert.IsFalse(errorLog.RawJson.ContainsKey("logger-attribute1"));
            Assert.AreEqual(1000, (long)errorLog.RawJson["logger-attribute2"]);
            Assert.AreEqual("value-1", (string)infoLog.RawJson["global-attribute-1"]);
            Assert.IsFalse(debugLog.RawJson.ContainsKey("global-attribute-2"));
            Assert.AreEqual("value", (string)errorLog.RawJson["attribute"]);
            Assert.AreEqual("user-id", errorLog.UserId);
            Assert.AreEqual("user-name", errorLog.UserName);
            Assert.IsNull(errorLog.UserEmail);
            Assert.AreEqual("property", errorLog.UserExtraInfo["extra"].ToString());


            var exceptionLog = logs[4];
            Assert.AreEqual("warn", exceptionLog.Status);
            Assert.AreEqual("Warning: this error occurred", exceptionLog.Message);
            Assert.AreEqual("logging.service", exceptionLog.ServiceName);
            CollectionAssert.DoesNotContain(exceptionLog.Tags, "tag1:tag-value");
            CollectionAssert.DoesNotContain(exceptionLog.Tags, "tag1:second-value");
            CollectionAssert.DoesNotContain(exceptionLog.Tags, "my-tag");
            Assert.IsFalse(exceptionLog.RawJson.ContainsKey("logger-attribute1"));
            Assert.AreEqual(1000, (long)exceptionLog.RawJson["logger-attribute2"]);
            Assert.AreEqual("value-1", (string)infoLog.RawJson["global-attribute-1"]);
            Assert.IsFalse(debugLog.RawJson.ContainsKey("global-attribute-2"));
            Assert.AreEqual("System.InvalidOperationException", exceptionLog.ErrorKind);
            Assert.AreEqual("Error Message", exceptionLog.ErrorMessage);
            Assert.NotNull(exceptionLog.ErrorStack);
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
                    RemoteSampleRate = 0.0f,
                    Name = "silent_logger",
                });
                silentLogger.Info("Interesting logging information");

                var loggingOptions = new DatadogLoggingOptions()
                {
                    Service = "logging.service",
                    Name = "not_silent_logger",
                };
                var logger = DatadogSdk.Instance.CreateLogger(loggingOptions);
                logger.AddTag("tag1", "tag-value");
                logger.AddTag("tag1", "second-value");
                logger.AddTag("my-tag");
                logger.AddAttribute("logger-attribute1", "string value");
                logger.AddAttribute("logger-attribute2", 1000);
                logger.Debug("debug message", new() { { "stringAttribute", "string" } });

                logger.RemoveTag("my-tag");
                DatadogSdk.Instance.AddLogsAttributes(new Dictionary<string, object>()
                {
                    { "global-attribute-1", "value-1" },
                    { "global-attribute-2", 1255 },
                });
                logger.Info("info message", new()
                {
                    {
                        "nestedAttribute", new Dictionary<string, object>()
                        {
                            { "internal", "test" },
                            { "isValid", true },
                        }
                    },
                });
                logger.Warn("warn message", new() { { "doubleAttribute", 10.34 } });

                logger.RemoveAttribute("logger-attribute1");
                logger.RemoveTagsWithKey("tag1");
                DatadogSdk.Instance.RemoveLogsAttribute("global-attribute-2");

                DatadogSdk.Instance.SetUserInfo("user-id", "user-name", extraInfo: new()
                {
                    { "extra", "property" },
                });

                logger.Error("error message", new() { { "attribute", "value" } });

                try
                {
                    throw new InvalidOperationException("Error Message");
                }
                catch (Exception e)
                {
                    logger.Warn("Warning: this error occurred", error: e);
                }

                _didSendLog = true;
            }
        }
    }
}
