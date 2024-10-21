// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Datadog.Unity.Tests
{
    public class DdUnityLogHandlerTests
    {
        private ILogHandler _originalLogHandler = null;
        private ILogHandler _mockLogger = null;
        private IInternalLogger _mockInternalLogger = null;

        [SetUp]
        public void SetUp()
        {
            _mockInternalLogger = Substitute.For<IInternalLogger>();
            DatadogSdk.Instance.InternalLogger = _mockInternalLogger;

            // Replace the default Unity logger with our
            _originalLogHandler = Debug.unityLogger.logHandler;
            _mockLogger = Substitute.For<ILogHandler>();
            Debug.unityLogger.logHandler = _mockLogger;
        }

        [TearDown]
        public void TearDown()
        {
            Debug.unityLogger.logHandler = _originalLogHandler;
            DatadogSdk.Instance.InternalLogger = null;
        }

        [Test]
        public void AttachReplacesUnityLogger()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger, null);

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
            var handler = new DdUnityLogHandler(datadogLogger, null);
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
            var handler = new DdUnityLogHandler(datadogLogger, null);

            // When
            handler.Detach();

            // Then
            Assert.AreEqual(_mockLogger, Debug.unityLogger.logHandler);
        }

        [Test]
        public void LogExceptionSendsToRum_WhenRumIsNotNull()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Critical, 100.0f);
            var rum = Substitute.For<IDdRum>();
            var handler = new DdUnityLogHandler(datadogLogger, rum);
            handler.Attach();

            // When
            var exception = new InvalidCastException("Fake Message");
            var context = new UnityEngine.Object();
            handler.LogException(exception, context);

            // Then
            rum.Received().AddError(exception, RumErrorSource.Source);
        }

        [Test]
        public void LogExceptionSendsToOriginalLogHandler()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Critical, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger, null);
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
            var handler = new DdUnityLogHandler(datadogLogger, null);
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
        public void LogExceptionSendsToTelemetry_WhenDatadogRumFails()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Critical, 100.0f);
            var rum = Substitute.For<IDdRum>();
            var handler = new DdUnityLogHandler(datadogLogger, rum);
            handler.Attach();
            var expectedException = new Exception();
            rum.When(rum =>
            {
                rum.AddError(
                    Arg.Any<Exception>(),
                    RumErrorSource.Source,
                    Arg.Any<Dictionary<string, object>>());
            }).Do(_ => throw expectedException);

            // When
            var exception = new InvalidCastException("Fake Message");
            var context = new UnityEngine.Object();
            handler.LogException(exception, context);

            // Then
            _mockInternalLogger.Received().TelemetryError(Arg.Any<string>(), expectedException);
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
            var handler = new DdUnityLogHandler(datadogLogger, null);
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
            var handler = new DdUnityLogHandler(datadogLogger, null);
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
        public void LogFormatSendsToTelemetry_WhenDatadogLoggerFails()
        {
            // Given
            var datadogLogger = Substitute.ForPartsOf<DdLogger>(DdLogLevel.Debug, 100.0f);
            var handler = new DdUnityLogHandler(datadogLogger, null);
            handler.Attach();
            var expectedException = new Exception();
            datadogLogger.When(logger =>
            {
                logger.PlatformLog(
                    DdLogLevel.Critical,
                    Arg.Any<string>(),
                    Arg.Any<Dictionary<string, object>>(),
                    Arg.Any<Exception>());
            }).Do(_ => throw expectedException);

            // When
            var context = new UnityEngine.Object();
            var args = new object[] { };
            handler.LogFormat(LogType.Assert, context, "format", args);

            // Then
            _mockInternalLogger.Received().TelemetryError(Arg.Any<string>(), expectedException);
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
            var handler = new DdUnityLogHandler(datadogLogger, null);
            handler.Attach();

            // When
            var context = new UnityEngine.Object();
            var args = new object[] { IInternalLogger.DatadogTag };
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
            var handler = new DdUnityLogHandler(datadogLogger, null);
            handler.Attach();

            // When
            var context = new UnityEngine.Object();
            var args = new object[] { IInternalLogger.DatadogTag };
            handler.LogFormat(logType, context, "{0} format", args);

            // Then
            _mockLogger.Received().LogFormat(logType, context, "{0} format", args);
        }
    }
}
