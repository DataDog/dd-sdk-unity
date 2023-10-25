// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Datadog.Unity.Tests
{
    public class DdUnityLogHandlerTests
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
        public void AttachReplacesUnityLogger()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);

            // When
            handler.Attach();

            // Then
            Assert.AreEqual(handler, Debug.unityLogger.logHandler);
        }

        [Test]
        public void DetachRevertsToOriginalLogger_WhenAttached()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);
            handler.Attach();

            // When
            handler.Detach();

            // Then
            Assert.AreEqual(_mockLogger, Debug.unityLogger.logHandler);
        }

        [Test]
        public void DetachDoesNothing_WhenAttachNotCalled()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Critical, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);

            // When
            handler.Detach();

            // Then
            Assert.AreEqual(_mockLogger, Debug.unityLogger.logHandler);
        }

        [Test]
        public void LogExceptionSendsToDatadog()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Critical, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);
            handler.Attach();

            // When
            var exception = new InvalidCastException("Fake Message");
            var context = new UnityEngine.Object();
            handler.LogException(exception, context);

            // Then
            datadogLogger.Received().PlatformLog(DdLogLevel.Critical, exception.Message, error: exception);
        }

        [Test]
        public void LogExceptionSendsToOriginalLogHandler()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Critical, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);
            handler.Attach();

            // When
            var exception = new InvalidCastException("Fake Message");
            var context = new UnityEngine.Object();
            handler.LogException(exception, context);

            // Then
            _mockLogger.Received().LogException(exception, context);
        }

        [Test]
        public void LogExceptionSendsToOriginalLogHandler_WhenDatadogLoggerFails()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Critical, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);
            handler.Attach();
            datadogLogger.When(logger =>
            {
                logger.PlatformLog(
                    DdLogLevel.Critical,
                    Arg.Any<string>(),
                    Arg.Any<Dictionary<string, object>>(),
                    Arg.Any<Exception>());
            }).Do(_ => throw new Exception());

            // When
            var exception = new InvalidCastException("Fake Message");
            var context = new UnityEngine.Object();
            handler.LogException(exception, context);

            // Then
            _mockLogger.Received().LogException(exception, context);
        }

        [Test]
        [TestCase(LogType.Error)]
        [TestCase(LogType.Assert)]
        [TestCase(LogType.Warning)]
        [TestCase(LogType.Log)]
        [TestCase(LogType.Exception)]
        public void LogFormatSendsToOriginalLogHandler(LogType logType)
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);
            handler.Attach();

            // When
            var exception = new InvalidCastException("Fake Message");
            var context = new UnityEngine.Object();
            var args = new object[] { };
            handler.LogFormat(logType, context, "format", args);

            // Then
            _mockLogger.Received().LogFormat(logType, context, "format", args);
        }

        [Test]
        public void LogFormatSendsToOriginalLogHandler_WhenDatadogLoggerFails()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);
            handler.Attach();
            datadogLogger.When(logger =>
            {
                logger.PlatformLog(
                    DdLogLevel.Critical,
                    Arg.Any<string>(),
                    Arg.Any<Dictionary<string, object>>(),
                    Arg.Any<Exception>());
            }).Do(_ => throw new Exception());

            // When
            var context = new UnityEngine.Object();
            var args = new object[] { };
            handler.LogFormat(LogType.Assert, context, "format", args);

            // Then
            _mockLogger.Received().LogFormat(LogType.Assert, context, "format", args);
        }

        [Test]
        [TestCase(LogType.Error)]
        [TestCase(LogType.Assert)]
        [TestCase(LogType.Warning)]
        [TestCase(LogType.Log)]
        [TestCase(LogType.Exception)]
        public void LogFormatDoesNotSendToDatadog_WhenTagIsDatadog(LogType logType)
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);
            handler.Attach();

            // When
            var context = new UnityEngine.Object();
            var args = new object[] { InternalLogger.DatadogTag };
            handler.LogFormat(logType, context, "{0} format", args);

            // Then
            datadogLogger.DidNotReceive().PlatformLog(
                Arg.Any<DdLogLevel>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<Exception>());
        }

        [Test]
        [TestCase(LogType.Error)]
        [TestCase(LogType.Assert)]
        [TestCase(LogType.Warning)]
        [TestCase(LogType.Log)]
        [TestCase(LogType.Exception)]
        public void LogFormatDoesNotSendToOriginalLogger_WhenTagIsDatadog(LogType logType)
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger);
            handler.Attach();

            // When
            var context = new UnityEngine.Object();
            var args = new object[] { InternalLogger.DatadogTag };
            handler.LogFormat(logType, context, "{0} format", args);

            // Then
            _mockLogger.Received().LogFormat(logType, context, "{0} format", args);
        }
    }
}
