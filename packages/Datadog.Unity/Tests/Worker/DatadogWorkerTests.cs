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
        private ThreadedWorker _worker;

        [SetUp]
        public void SetUp()
        {
            _mockProcessor = Substitute.For<IDatadogWorkerProcessor>();
            _mockLogger = Substitute.For<IInternalLogger>();
            _worker = new ThreadedWorker(_mockLogger);
        }

        [TearDown]
        public void TearDown()
        {
            _worker.Stop();
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
