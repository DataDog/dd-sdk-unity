// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Threading;
using Datadog.Unity.Core;
using NSubstitute;
using NUnit.Framework;

namespace Datadog.Unity.Worker.Tests
{
    public class DatadogThreadedWorkerTests
    {
        private IDatadogWorkerProcessor _mockProcessor;
        private IInternalLogger _mockLogger;
        private IInternalLogger _originalLogger;
        private ThreadedWorker _worker;

        [SetUp]
        public void SetUp()
        {
            _originalLogger = DatadogSdk.Instance.InternalLogger;
            _mockProcessor = Substitute.For<IDatadogWorkerProcessor>();
            _mockLogger = Substitute.For<IInternalLogger>();
            DatadogSdk.Instance.InternalLogger = _mockLogger;
            _worker = new ThreadedWorker(_mockLogger);
        }

        [TearDown]
        public void TearDown()
        {
            _worker.Stop();
            DatadogSdk.Instance.InternalLogger = _originalLogger;
        }

        [Test]
        public void WorkerSendsMessagesToProcessor()
        {
            _worker.AddProcessor(MockWorkerMessage.ProcessorName, _mockProcessor);

            // Can add messages before the worker is started, they will be processed when the worker starts
            var message = new MockWorkerMessage("fake data");
            _worker.AddMessage(message);
            _mockProcessor.DidNotReceive().Process(message);

            _worker.Start();

            // Yield to the processing thread
            Thread.Sleep(10);

            _mockProcessor.Received(1).Process(message);
        }

        [Test]
        public void WorkerDiscardsMessagesWhenFinished()
        {
            _worker.AddProcessor(MockWorkerMessage.ProcessorName, _mockProcessor);

            // Can add messages before the worker is started, they will be processed when the worker starts
            var message = new MockWorkerMessage("fake data");
            _worker.AddMessage(message);
            _mockProcessor.DidNotReceive().Process(message);

            _worker.Start();

            // Yield to the processing thread
            Thread.Sleep(10);

            Assert.IsTrue(message.WasDiscarded);
        }

        [Test]
        public void StoppedWorkerFinishesSendingMessages()
        {
            _worker.AddProcessor(MockWorkerMessage.ProcessorName, _mockProcessor);

            var message = new MockWorkerMessage("fake data");
            _worker.AddMessage(message);
            _mockProcessor.DidNotReceive().Process(message);
            _worker.Start();
            _worker.Stop();

            _mockProcessor.Received(1).Process(message);
        }

        [Test]
        public void WorkerThreadCatchesProcessorExceptions()
        {
            // Given
            _worker.AddProcessor(MockWorkerMessage.ProcessorName, _mockProcessor);
            _mockProcessor
                .When(p => p.Process(Arg.Any<IDatadogWorkerMessage>()))
                .Throw(x => new InvalidCastException("Test Exception"));
            _worker.Start();

            // When
            var messageA = new MockWorkerMessage("Fake message A");
            _worker.AddMessage(messageA);
            var messageB = new MockWorkerMessage("Fake message B");
            _worker.AddMessage(messageB);

            _worker.Stop();

            _mockProcessor.Received(1).Process(messageB);
        }

        [Test]
        public void WorkerThreadReportsExceptionMessagesAndStacks()
        {
            var platform = Substitute.For<IDatadogPlatform>();
            DatadogSdk.Instance.InternalLogger = new InternalLogger(_worker, platform);
            _worker.AddProcessor(MockWorkerMessage.ProcessorName, _mockProcessor);
            var exception = new FormatException("Discard failed", new ArgumentException("Invalid message"));
            var failingMessage = Substitute.For<IDatadogWorkerMessage>();
            failingMessage.FeatureTarget.Returns(MockWorkerMessage.ProcessorName);
            failingMessage.When(m => m.Discard()).Throw(exception);

            _worker.Start();
            _worker.AddMessage(failingMessage);
            _worker.Stop();

            platform.Received(1).SendErrorTelemetry(
                "Exception on worker thread",
                Arg.Is<string>(stack => stack.Contains("System.FormatException: Discard failed") &&
                                        stack.Contains("System.ArgumentException: Invalid message") &&
                                        stack.Contains(exception.StackTrace)),
                "System.FormatException");
        }

        [Test]
        public void TelemetryOnWorkerIsSentBeforeProcessorReturns()
        {
            var platform = Substitute.For<IDatadogPlatform>();
            DatadogSdk.Instance.InternalLogger = new InternalLogger(_worker, platform);
            var telemetrySent = false;
            var sentBeforeReturn = false;
            platform.When(p => p.SendErrorTelemetry("Telemetry from worker", Arg.Any<string>(), Arg.Any<string>()))
                .Do(_ => telemetrySent = true);
            _worker.AddProcessor(MockWorkerMessage.ProcessorName, _mockProcessor);
            _mockProcessor.When(p => p.Process(Arg.Any<IDatadogWorkerMessage>()))
                .Do(_ =>
                {
                    DatadogSdk.Instance.InternalLogger.TelemetryError(
                        "Telemetry from worker", new InvalidOperationException("Test exception"));
                    sentBeforeReturn = telemetrySent;
                });

            _worker.Start();
            _worker.AddMessage(new MockWorkerMessage("Test message"));
            _worker.Stop();

            Assert.IsTrue(sentBeforeReturn);
        }

        [Test]
        public void WorkerThreadRestartsIfStopped()
        {
            // Given
            _worker.AddProcessor(MockWorkerMessage.ProcessorName, _mockProcessor);
            var messageA = new MockWorkerMessage("Fake Message A");
            _mockProcessor.When(p => p.Process(messageA))
                .Throw(new InvalidCastException("Message A"));

            // When
            _worker.Start();
            _worker.AddMessage(messageA);
            _worker.Kill();
            Assert.IsFalse(_worker.IsAlive);

            var messageB = new MockWorkerMessage("Fake Message B");
            _worker.AddMessage(messageB);
            _worker.Stop();

            // Then
            _mockProcessor.Received(1).Process(messageB);
        }
    }

    internal class MockWorkerMessage : IDatadogWorkerMessage
    {
        public const string ProcessorName = "mock";

        public MockWorkerMessage(string data)
        {
            Data = data;
            WasDiscarded = false;
        }

        public string FeatureTarget => ProcessorName;

        public string Data { get; set; }

        public bool WasDiscarded { get; private set; }

        public void Discard()
        {
            WasDiscarded = true;
        }
    }
}
