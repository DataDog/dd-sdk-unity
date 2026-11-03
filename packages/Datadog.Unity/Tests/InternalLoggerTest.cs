// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Threading;
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
        public void LogForwardsToUnityLoggerWithDatadogTag(LogType logType, DdLogLevel ddLogLevel)
        {
            // Given
            var mockInternalLogger = Substitute.For<IInternalLogger>();
            var worker = new ThreadedWorker(mockInternalLogger);
            var fakePlatform = Substitute.For<IDatadogPlatform>();
            var internalLogger = new InternalLogger(worker, fakePlatform);

            // When
            internalLogger.Log(ddLogLevel, "Fake Message");

            // Then
            _mockLogger.Received().LogFormat(
                logType,
                null,
                "{0}: {1}",
                IInternalLogger.DatadogTag,
                "Fake Message");
        }

        [Test]
        public void TelemetryDebugForwardsLogToPlatformThroughWorker()
        {
            // Given
            var mockInternalLogger = Substitute.For<IInternalLogger>();
            var worker = new ThreadedWorker(mockInternalLogger);
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            var internalLogger = new InternalLogger(worker, mockPlatform);

            // When
            internalLogger.TelemetryDebug("Telemetry debug message");

            // Then
            mockPlatform.DidNotReceive().SendDebugTelemetry(Arg.Any<string>());

            // When
            worker.Start();
            Thread.Sleep(10);

            // Then
            mockPlatform.Received().SendDebugTelemetry("Telemetry debug message");
            worker.Stop();
        }

        [Test]
        public void TelemetryErrorForwardsLogToPlatformThroughWorker()
        {
            // Given
            var mockInternalLogger = Substitute.For<IInternalLogger>();
            var worker = new ThreadedWorker(mockInternalLogger);
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            var internalLogger = new InternalLogger(worker, mockPlatform);

            // When
            InvalidOperationException thrownException;
            try
            {
                // Throw to populate the stack trace
                throw new InvalidOperationException("Did do invalid operation");
            }
            catch (InvalidOperationException e)
            {
                thrownException = e;
                internalLogger.TelemetryError("Telemetry error message", e);
            }

            // Then
            mockPlatform.DidNotReceive().SendErrorTelemetry(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>());

            // When
            worker.Start();
            worker.Stop();

            // Then
            mockPlatform.Received().SendErrorTelemetry(
                "Telemetry error message",
                thrownException.ToString(),
                "System.InvalidOperationException");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TelemetryErrorPreservesExceptionMessagesAndStacks(bool withInnerException)
        {
            var innerException = withInnerException
                ? PopulateStackTrace(new ArgumentException("Inner exception message"))
                : null;
            var exception = PopulateStackTrace(new InvalidOperationException("Outer exception message", innerException));

            var stack = SendTelemetryError(exception);

            StringAssert.Contains("System.InvalidOperationException: Outer exception message", stack);
            StringAssert.Contains(exception.StackTrace, stack);
            if (innerException != null)
            {
                StringAssert.Contains("System.ArgumentException: Inner exception message", stack);
                StringAssert.Contains(innerException.StackTrace, stack);
            }
        }

        [Test]
        public void TelemetryErrorPreservesExceptionMessageWithoutStackTrace()
        {
            var exception = new InvalidOperationException("Exception without stack trace");
            Assert.IsNull(exception.StackTrace);

            var stack = SendTelemetryError(exception);

            StringAssert.Contains("System.InvalidOperationException: Exception without stack trace", stack);
        }

        [Test]
        public void TelemetryErrorFallsBackToStackWhenToStringThrows()
        {
            var exception = PopulateStackTrace(new ThrowingToStringException());
            Assert.Throws<InvalidOperationException>(() => exception.ToString());

            var stack = SendTelemetryError(exception);

            Assert.IsNotEmpty(stack);
            Assert.AreEqual(exception.StackTrace, stack);
        }

        [Test]
        public void TelemetryErrorFallsBackToStackWhenMessageThrows()
        {
            var exception = PopulateStackTrace(new ThrowingMessageException());
            Assert.Throws<InvalidOperationException>(() => exception.ToString());

            var stack = SendTelemetryError(exception);

            Assert.IsNotEmpty(stack);
            Assert.AreEqual(exception.StackTrace, stack);
        }

        [Test]
        public void TelemetryErrorStillSendsWhenFormattingAndStackTraceThrow()
        {
            var exception = new ThrowingStackTraceException();
            Assert.Throws<InvalidOperationException>(() => exception.ToString());

            var stack = SendTelemetryError(exception);

            Assert.IsNull(stack);
        }

        [Test]
        public void TelemetryErrorForwardsLogToPlatformThroughWorker_WithNullException()
        {
            // Given
            var mockInternalLogger = Substitute.For<IInternalLogger>();
            var worker = new ThreadedWorker(mockInternalLogger);
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            var internalLogger = new InternalLogger(worker, mockPlatform);

            // When
            internalLogger.TelemetryError("Telemetry error message", null);

            // Then
            mockPlatform.DidNotReceive().SendErrorTelemetry(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>());

            // When
            worker.Start();
            Thread.Sleep(10);

            // Then
            mockPlatform.Received().SendErrorTelemetry("Telemetry error message",  null, null);
            worker.Stop();
        }

        private static string SendTelemetryError(Exception exception)
        {
            var platform = Substitute.For<IDatadogPlatform>();
            string stack = null;
            platform.When(p => p.SendErrorTelemetry(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
                .Do(call => stack = call.ArgAt<string>(1));
            var logger = new InternalLogger(new PassthroughWorker(), platform);

            logger.TelemetryError("Telemetry error message", exception);

            platform.Received(1).SendErrorTelemetry(
                "Telemetry error message", Arg.Any<string>(), exception.GetType().ToString());
            return stack;
        }

        private static Exception PopulateStackTrace(Exception exception)
        {
            try
            {
                throw exception;
            }
            catch (Exception e)
            {
                return e;
            }
        }

        private class ThrowingToStringException : Exception
        {
            public override string ToString() => throw new InvalidOperationException("ToString failed");
        }

        private class ThrowingMessageException : Exception
        {
            public override string Message => throw new InvalidOperationException("Message failed");
        }

        private class ThrowingStackTraceException : ThrowingToStringException
        {
            public override string StackTrace => throw new InvalidOperationException("StackTrace failed");
        }
    }
}
