// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Threading;
using NSubstitute;
using NUnit.Framework;

namespace Datadog.Unity.Worker.Tests
{
    public class DatadogWorkerTests
    {
        private IDatadogWorkerProcessor _mockProcessor;
        private DatadogWorker _worker;

        [SetUp]
        public void SetUp()
        {
            _mockProcessor = Substitute.For<IDatadogWorkerProcessor>();
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
