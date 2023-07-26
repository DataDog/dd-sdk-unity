// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Threading;
using Moq;
using NUnit.Framework;

namespace Datadog.Unity.Worker.Tests
{
    public class DatadogWorkerTests
    {
        private Mock<IDatadogWorkerProcessor> _mockProcessor;
        private DatadogWorker _worker;

        [SetUp]
        public void SetUp()
        {
            _mockProcessor = new();
            _worker = new();
        }

        [TearDown]
        public void TearDown()
        {
            _worker.Stop();
        }

        [Test]
        public void WorkerSendsMessagesToProcessor()
        {
            _worker.AddProcessor(MockWorkerMessage.ProcessorName, _mockProcessor.Object);

            // Can add messages before the worker is started, they will be processed when the worker starts
            var message = new MockWorkerMessage("fake data");
            _worker.AddMessage(message);
            _mockProcessor.Verify(m => m.Process(message), Times.Never);

            _worker.Start();

            // Yield to the procssing thread
            Thread.Sleep(10);

            _mockProcessor.Verify(m => m.Process(message), Times.Once);
        }

        [Test]
        public void StoppedWorkerFinishesSendingMessages()
        {
            _worker.AddProcessor(MockWorkerMessage.ProcessorName, _mockProcessor.Object);

            var message = new MockWorkerMessage("fake data");
            _worker.AddMessage(message);
            _mockProcessor.Verify(m => m.Process(message), Times.Never);
            _worker.Start();
            _worker.Stop();

            _mockProcessor.Verify(m => m.Process(message), Times.Once);
        }
    }

    internal class MockWorkerMessage : IDatadogWorkerMessage
    {
        public const string ProcessorName = "mock";

        public MockWorkerMessage(string data)
        {
            Data = data;
        }

        public string FeatureTarget => ProcessorName;

        public string Data { get; set; }
    }
}
