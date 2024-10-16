// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Datadog.Unity.Logs;
using Datadog.Unity.Worker;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests
{
    public class LoggingTests
    {
        [TearDown]
        public void TearDown()
        {
            DatadogSdk.Shutdown();
        }

#if UNITY_ANDROID
        [TestCase(DdLogLevel.Debug, AndroidLogLevel.Debug)]
        [TestCase(DdLogLevel.Info, AndroidLogLevel.Info)]
        [TestCase(DdLogLevel.Notice, AndroidLogLevel.Info)]
        [TestCase(DdLogLevel.Warn, AndroidLogLevel.Warn)]
        [TestCase(DdLogLevel.Error, AndroidLogLevel.Error)]
        [TestCase(DdLogLevel.Critical, AndroidLogLevel.Assert)]
        public void DdLogLevelTranslatedToAndroidLogLevel(DdLogLevel ddLogLevel, int androidLogLevel)
        {
            var translated = InternalHelpers.DdLogLevelToAndroidLogLevel(ddLogLevel);
            Assert.AreEqual(androidLogLevel, (int)translated);
        }
#endif

        [TestCase(LogType.Error, DdLogLevel.Error)]
        [TestCase(LogType.Assert, DdLogLevel.Critical)]
        [TestCase(LogType.Warning, DdLogLevel.Warn)]
        [TestCase(LogType.Log, DdLogLevel.Info)]
        [TestCase(LogType.Exception, DdLogLevel.Critical)]
        public void UnityLogLevelTranslatedToDdLogLevel(LogType logType, DdLogLevel ddLogLevel)
        {
            var translated = DdLogHelpers.LogTypeToDdLogLevel(logType);
            Assert.AreEqual(ddLogLevel, translated);
        }

        [Test]
        public void DatadogInitCreatesDefaultLogger()
        {
            // Given
            var mockLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            mockPlatform
                .CreateLogger(Arg.Any<DatadogLoggingOptions>(), Arg.Any<DatadogWorker>())
                .Returns(mockLogger);

            // When
            DatadogSdk.InitWithPlatform(mockPlatform, new());

            // Then
            mockPlatform.Received().CreateLogger(Arg.Any<DatadogLoggingOptions>(), Arg.Any<DatadogWorker>());
            Assert.AreEqual(DatadogSdk.Instance.DefaultLogger, mockLogger);
        }

        [Test]
        public void UnityLogsAreNotForwardedToDefaultLogger_WhenForwardUnityLogsIsFalse()
        {
            var mockLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            mockPlatform
                .CreateLogger(Arg.Any<DatadogLoggingOptions>(), Arg.Any<DatadogWorker>())
                .Returns(mockLogger);

            DatadogSdk.InitWithPlatform(mockPlatform, new()
            {
                ForwardUnityLogs = false,
            });

            Debug.Log("Test Logs");

            mockLogger.DidNotReceiveWithAnyArgs().PlatformLog(default, default);
        }

        [Test]
        public void UnityLogsAreForwardedToDefaultLogger_WhenForwardUnityLogsIsTrue()
        {
            var mockUnityLogger = Substitute.For<ILogHandler>();
            Debug.unityLogger.logHandler = mockUnityLogger;

            var mockLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            mockPlatform
                .CreateLogger( Arg.Any<DatadogLoggingOptions>(), Arg.Any<DatadogWorker>())
                .Returns(mockLogger);

            DatadogSdk.InitWithPlatform(mockPlatform, new()
            {
                ForwardUnityLogs = true,
            });

            Debug.Log("Test Logs");

            mockLogger.Received().PlatformLog(DdLogLevel.Info, "Test Logs", null, null);
        }

        [Test]
        public void UnityLogsAreForwardedToDefaultLoggerWithProperLevel()
        {
            LogAssert.ignoreFailingMessages = true;
            var mockUnityLogger = Substitute.For<ILogHandler>();
            Debug.unityLogger.logHandler = mockUnityLogger;

            var mockLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            mockPlatform
                .CreateLogger(Arg.Any<DatadogLoggingOptions>(), Arg.Any<DatadogWorker>())
                .Returns(mockLogger);

            DatadogSdk.InitWithPlatform(mockPlatform, new()
            {
                ForwardUnityLogs = true,
            });

            Debug.Log("Test Logs");
            Debug.LogError("Test LogError");
            Debug.LogWarning("Test LogWarning");
            Debug.LogAssertion("Test LogAssertion");

            mockLogger.Received().PlatformLog(DdLogLevel.Info, "Test Logs", null, null);
            mockLogger.Received().PlatformLog(DdLogLevel.Error, "Test LogError", null, null);
            mockLogger.Received().PlatformLog(DdLogLevel.Warn, "Test LogWarning", null, null);
            mockLogger.Received().PlatformLog(DdLogLevel.Critical, "Test LogAssertion", null, null);
        }

        [Test]
        public void UnityLogsAreForwardsLogsBackToUnity()
        {
            // Given
            LogAssert.ignoreFailingMessages = true;
            var mockUnityLogger = Substitute.For<ILogHandler>();
            Debug.unityLogger.logHandler = mockUnityLogger;

            var mockLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var messageArgs = new List<string>();

            // When
            Debug.Log("Test Logs");
            Debug.LogError("Test LogError");
            Debug.LogWarning("Test LogWarning");
            Debug.LogAssertion("Test LogAssertion");

            // Then
            // Don't ask me why, but this is how Unity formats its messages by default
            mockUnityLogger.Received().LogFormat(LogType.Log, null, "{0}", "Test Logs");
            mockUnityLogger.Received().LogFormat(LogType.Error, null, "{0}", "Test LogError");
            mockUnityLogger.Received().LogFormat(LogType.Warning, null, "{0}", "Test LogWarning");
            mockUnityLogger.Received().LogFormat(LogType.Assert, null, "{0}", "Test LogAssertion");
        }

        [Test]
        public void CreateLoggerForwardsRequestToPlatform()
        {
            // Given
            var mockLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            mockPlatform
                .CreateLogger(Arg.Any<DatadogLoggingOptions>(), Arg.Any<DatadogWorker>())
                .Returns(mockLogger);
            DatadogSdk.InitWithPlatform(mockPlatform, new());

            // When
            var options = new DatadogLoggingOptions()
            {
                Name = "test_logger",
                RemoteLogThreshold = DdLogLevel.Warn,
                RemoteSampleRate = 100.0f,
            };
            var logger = DatadogSdk.Instance.CreateLogger(options);

            // Then
            mockPlatform.Received().CreateLogger(options, Arg.Any<DatadogWorker>());
            Assert.AreEqual(logger, mockLogger);
        }

        [Test]
        public void GlobalAttributeIsForwardedToPlatform()
        {
            // Given
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            Dictionary<string, object> callbackAttributes = null;
            mockPlatform.AddLogsAttributes(Arg.Do<Dictionary<string, object>>(attributes =>
            {
                callbackAttributes = new Dictionary<string, object>();
                callbackAttributes.Copy(attributes);
            }));
            DatadogSdk.InitWithPlatform(mockPlatform, new());

            // When
            DatadogSdk.Instance.AddLogsAttribute("fake_key", "fake_value");
            Thread.Sleep(10);

            // Then
            mockPlatform.Received().AddLogsAttributes(Arg.Any<Dictionary<string,object>>());
            var expected = new Dictionary<string, object>()
            {
                { "fake_key", "fake_value" },
            };
            CollectionAssert.AreEquivalent(expected, callbackAttributes);
        }

        [Test]
        public void GlobalAttributesAreForwardedToPlatform()
        {
            // Given
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            Dictionary<string, object> callbackAttributes = null;
            mockPlatform.AddLogsAttributes(Arg.Do<Dictionary<string, object>>(attributes =>
            {
                callbackAttributes = new Dictionary<string, object>();
                callbackAttributes.Copy(attributes);
            }));
            DatadogSdk.InitWithPlatform(mockPlatform, new());

            // When
            var newAttributes = new Dictionary<string, object>()
            {
                { "key_1", "value_1" },
                { "key_2", "value_2" },
                { "number_key", 12555 },
                { "other_key", 2.3333 },
            };
            DatadogSdk.Instance.AddLogsAttributes(newAttributes);
            Thread.Sleep(10);

            // Then
            mockPlatform.Received().AddLogsAttributes(Arg.Any<Dictionary<string,object>>());
            CollectionAssert.AreEquivalent(newAttributes, callbackAttributes);
        }

        [Test]
        public void GlobalAttributesRejectsAddWithNullKey()
        {
            // Given
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            DatadogSdk.InitWithPlatform(mockPlatform, new());

            // When
            DatadogSdk.Instance.AddLogsAttribute(null, "value");

            // Then
            mockPlatform.DidNotReceive().AddLogsAttributes(Arg.Any<Dictionary<string, object>>());
        }

        [Test]
        public void GlobalAttributesRemoveIsForwardedToPlatform()
        {
            // Given
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            DatadogSdk.InitWithPlatform(mockPlatform, new());

            // When
            DatadogSdk.Instance.RemoveLogsAttribute("fake_key");
            Thread.Sleep(10);

            // Then
            mockPlatform.Received().RemoveLogsAttribute("fake_key");
        }

        [Test]
        public void GlobalAttributesRejectsRemoveWithNullKey()
        {
            // Given
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            DatadogSdk.InitWithPlatform(mockPlatform, new());

            // When
            DatadogSdk.Instance.RemoveLogsAttribute(null);

            // Then
            mockPlatform.DidNotReceive().RemoveLogsAttribute(Arg.Any<string>());
        }

        [Test]
        public void LoggerForwardsCorrectLevelsToLog()
        {
            // Given
            var mockLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var logArgs = new List<DdLogLevel>();
            var messageArgs = new List<string>();
            mockLogger.PlatformLog(
                Arg.Do<DdLogLevel>(l => logArgs.Add(l)),
                Arg.Do<string>(m => messageArgs.Add(m)),
                null,
                null);

            // When
            mockLogger.Debug("debug message");
            mockLogger.Info("info message");
            mockLogger.Notice("notice message");
            mockLogger.Warn("warn message");
            mockLogger.Error("error message");
            mockLogger.Critical("critical message");

            Assert.IsTrue(logArgs.SequenceEqual(
                new[] { DdLogLevel.Debug, DdLogLevel.Info, DdLogLevel.Notice, DdLogLevel.Warn, DdLogLevel.Error, DdLogLevel.Critical }));
            Assert.IsTrue(messageArgs.SequenceEqual(
                new[] { "debug message", "info message", "notice message", "warn message", "error message", "critical message" }));
        }

        [Test]
        public void LoggerDoesNotSendSampledLogsToPlatform()
        {
            // Given
            var mockLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 0.0f);

            // When
            mockLogger.Debug("debug message");
            mockLogger.Info("info message");
            mockLogger.Notice("notice message");
            mockLogger.Warn("warn message");
            mockLogger.Error("error message");
            mockLogger.Critical("critical message");

            mockLogger.DidNotReceive().PlatformLog(Arg.Any<DdLogLevel>(), Arg.Any<string>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<Exception>());
        }

        [Test]
        public void LoggerDoesNotSendLogsToPlatform_WhenBelowThreshold()
        {
            // Given
            var mockLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Warn, 100.0f);

            // When
            mockLogger.Debug("debug message");
            mockLogger.Info("info message");
            mockLogger.Notice("notice message");
            mockLogger.Warn("warn message");
            mockLogger.Error("error message");
            mockLogger.Critical("critical message");

            mockLogger.DidNotReceive().PlatformLog(DdLogLevel.Debug, "debug message");
            mockLogger.DidNotReceive().PlatformLog(DdLogLevel.Info, "info message");
            mockLogger.DidNotReceive().PlatformLog(DdLogLevel.Notice, "notice message");
            mockLogger.Received().PlatformLog(DdLogLevel.Warn, "warn message");
            mockLogger.Received().PlatformLog(DdLogLevel.Error, "error message");
            mockLogger.Received().PlatformLog(DdLogLevel.Critical, "critical message");
        }
    }
}
