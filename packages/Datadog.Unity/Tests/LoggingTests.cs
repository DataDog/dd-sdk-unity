// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
#if UNITY_ANDROID
using Datadog.Unity.Android;
#endif
using Datadog.Unity.Logs;
using Moq;
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
            var translated = DatadogConfigurationHelpers.DdLogLevelToAndroidLogLevel(ddLogLevel);
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
            var mockLogger = new Mock<IDdLogger>();
            var mockPlatform = new Mock<IDatadogPlatform>();
            mockPlatform
                .Setup(m => m.CreateLogger(It.IsAny<DatadogLoggingOptions>()))
                .Returns(mockLogger.Object);

            // When
            DatadogSdk.InitWithPlatform(mockPlatform.Object, new());

            // Then
            mockPlatform.Verify(m => m.CreateLogger(It.IsAny<DatadogLoggingOptions>()), Times.Once);
            Assert.AreEqual(DatadogSdk.Instance.DefaultLogger, mockLogger.Object);
        }

        [Test]
        public void UnityLogsAreNotForwardedToDefaultLogger_WhenForwardUnityLogsIsFalse()
        {
            var mockLogger = new Mock<IDdLogger>();
            var mockPlatform = new Mock<IDatadogPlatform>();
            mockPlatform
                .Setup(m => m.CreateLogger(It.IsAny<DatadogLoggingOptions>()))
                .Returns(mockLogger.Object);

            DatadogSdk.InitWithPlatform(mockPlatform.Object, new()
            {
                ForwardUnityLogs = false,
            });

            Debug.Log("Test Logs");

            mockLogger.VerifyNoOtherCalls();
        }

        [Test]
        public void UnityLogsAreForwardedToDefaultLogger_WhenForwardUnityLogsIsTrue()
        {
            var mockLogger = new Mock<IDdLogger>();
            var mockPlatform = new Mock<IDatadogPlatform>();
            mockPlatform
                .Setup(m => m.CreateLogger(It.IsAny<DatadogLoggingOptions>()))
                .Returns(mockLogger.Object);

            DatadogSdk.InitWithPlatform(mockPlatform.Object, new()
            {
                ForwardUnityLogs = true,
            });

            Debug.Log("Test Logs");

            mockLogger.Verify(l => l.Log(DdLogLevel.Info, "Test Logs", null, null));
        }

        [Test]
        public void UnityLogsAreForwardedToDefaultLoggerWithProperLevel()
        {
            LogAssert.ignoreFailingMessages = true;

            var mockLogger = new Mock<IDdLogger>();
            var mockPlatform = new Mock<IDatadogPlatform>();
            mockPlatform
                .Setup(m => m.CreateLogger(It.IsAny<DatadogLoggingOptions>()))
                .Returns(mockLogger.Object);

            DatadogSdk.InitWithPlatform(mockPlatform.Object, new()
            {
                ForwardUnityLogs = true,
            });

            Debug.Log("Test Logs");
            Debug.LogError("Test LogError");
            Debug.LogWarning("Test LogWarning");
            Debug.LogAssertion("Test LogAssertion");

            mockLogger.Verify(l => l.Log(DdLogLevel.Info, "Test Logs", null, null));
            mockLogger.Verify(l => l.Log(DdLogLevel.Error, "Test LogError", null, null));
            mockLogger.Verify(l => l.Log(DdLogLevel.Warn, "Test LogWarning", null, null));
            mockLogger.Verify(l => l.Log(DdLogLevel.Critical, "Test LogAssertion", null, null));
        }

        [Test]
        public void CreateLoggerForwardsRequestToPlatform()
        {
            // Given
            var mockLogger = new Mock<IDdLogger>();
            var mockPlatform = new Mock<IDatadogPlatform>();
            mockPlatform
                .Setup(m => m.CreateLogger(It.IsAny<DatadogLoggingOptions>()))
                .Returns(mockLogger.Object);
            DatadogSdk.InitWithPlatform(mockPlatform.Object, new());

            // When
            var options = new DatadogLoggingOptions()
            {
                LoggerName = "test_logger",
                DatadogReportingThreshold = DdLogLevel.Warn,
                SendToDatadog = false,
            };
            var logger = DatadogSdk.Instance.CreateLogger(options);

            // Then
            mockPlatform.Verify(m => m.CreateLogger(options));
            Assert.AreEqual(logger, mockLogger.Object);
        }

        [Test]
        public void LoggerForwardsCorrectLevelsToLog()
        {
            // Given
            var mockLogger = new Mock<IDdLogger>();
            var logArgs = new List<DdLogLevel>();
            var messageArgs = new List<string>();
            mockLogger.Setup(m => m.Log(Capture.In(logArgs), Capture.In(messageArgs), null, null));

            // When
            mockLogger.Object.Debug("debug message");
            mockLogger.Object.Info("info message");
            mockLogger.Object.Notice("notice message");
            mockLogger.Object.Warn("warn message");
            mockLogger.Object.Error("error message");
            mockLogger.Object.Critical("critical message");

            Assert.IsTrue(logArgs.SequenceEqual(
                new[] { DdLogLevel.Debug, DdLogLevel.Info, DdLogLevel.Notice, DdLogLevel.Warn, DdLogLevel.Error, DdLogLevel.Critical }));
            Assert.IsTrue(messageArgs.SequenceEqual(
                new[] { "debug message", "info message", "notice message", "warn message", "error message", "critical message" }));
        }
    }
}
