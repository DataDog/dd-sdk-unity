// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Datadog.Unity.Worker;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;

namespace Datadog.Unity.Tests
{
    public class InternalLoggerTest
    {
        private ILogHandler _originalLogHandler = null;
        private ILogHandler _mockLogger = null;

        [SetUp]
        public void SetUp()
        {
            // Replace the default Unity logger with our
            _originalLogHandler = Debug.unityLogger.logHandler;
            _mockLogger = Substitute.For<ILogHandler>();
            Debug.unityLogger.logHandler = _mockLogger;
        }

        [TearDown]
        public void TearDown()
        {
            Debug.unityLogger.logHandler = _originalLogHandler;
        }

        [Test]
        [TestCase(LogType.Exception, DdLogLevel.Critical)]
        [TestCase(LogType.Error, DdLogLevel.Error)]
        [TestCase(LogType.Warning, DdLogLevel.Warn)]
        [TestCase(LogType.Log, DdLogLevel.Info)]
        [TestCase(LogType.Log, DdLogLevel.Notice)]
        [TestCase(LogType.Log, DdLogLevel.Debug)]
        public void ForwardsToUnityLoggerWithDatadogTag(LogType logType, DdLogLevel ddLogLevel)
        {
            // Given
            var worker = new DatadogWorker();
            var fakePlatform = Substitute.For<IDatadogPlatform>();
            var internalLogger = new InternalLogger();

            // When
            internalLogger.Log(ddLogLevel, "Fake Message");

            // Then
            _mockLogger.Received().LogFormat(
                logType,
                null,
                "{0}: {1}",
                InternalLogger.DatadogTag,
                "Fake Message");
        }
    }
}
