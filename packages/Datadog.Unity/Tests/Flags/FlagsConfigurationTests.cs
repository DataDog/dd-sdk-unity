// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class FlagsConfigurationTests
    {
        [Test]
        public void DefaultsToNoAssignmentRequestTimeoutAndNoRetries()
        {
            var configuration = new FlagsConfiguration();

            Assert.AreEqual(0, configuration.AssignmentRequestTimeoutSeconds);
            Assert.AreEqual(0, configuration.AssignmentRequestRetryCount);
        }

        [Test]
        public async Task DefaultConfigurationMakesExactlyOneInitialRequest()
        {
            var configuration = new FlagsConfiguration();
            var inner = new StubTransport(new AssignmentResponse(
                AssignmentRequestResult.ConnectionError,
                0));
            var transport = AssignmentRequestTransports.BuildRetryTransport(
                inner,
                configuration.AssignmentRequestRetryCount);

            Assert.AreSame(inner, transport);
            await transport.SendAsync(new AssignmentRequest(
                "POST",
                "https://example.com/assignments",
                "body",
                null));

            Assert.AreEqual(1, inner.SendCount);
        }

        [Test]
        public void AcceptsCustomAssignmentRequestLimits()
        {
            var configuration = new FlagsConfiguration(
                assignmentRequestTimeoutSeconds: 3,
                assignmentRequestRetryCount: 2);

            Assert.AreEqual(3, configuration.AssignmentRequestTimeoutSeconds);
            Assert.AreEqual(2, configuration.AssignmentRequestRetryCount);
        }

        [Test]
        public void ZeroDisablesAssignmentRequestLimits()
        {
            var configuration = new FlagsConfiguration(
                assignmentRequestTimeoutSeconds: 0,
                assignmentRequestRetryCount: 0);

            Assert.AreEqual(0, configuration.AssignmentRequestTimeoutSeconds);
            Assert.AreEqual(0, configuration.AssignmentRequestRetryCount);
        }

        [TestCase(-1, -1, 0, 0)]
        [TestCase(1, 11, 1, 10)]
        [TestCase(
            AssignmentRequestRetryPolicy.MaxTimeoutSeconds,
            1,
            AssignmentRequestRetryPolicy.MaxTimeoutSeconds,
            1)]
        [TestCase(
            AssignmentRequestRetryPolicy.MaxTimeoutSeconds + 1,
            1,
            AssignmentRequestRetryPolicy.MaxTimeoutSeconds,
            1)]
        [TestCase(
            int.MaxValue,
            1,
            AssignmentRequestRetryPolicy.MaxTimeoutSeconds,
            1)]
        public void NormalizesInvalidAssignmentRequestLimits(
            int timeoutSeconds,
            int retryCount,
            int expectedTimeoutSeconds,
            int expectedRetryCount)
        {
            var configuration = new FlagsConfiguration(
                assignmentRequestTimeoutSeconds: timeoutSeconds,
                assignmentRequestRetryCount: retryCount);

            Assert.AreEqual(expectedTimeoutSeconds, configuration.AssignmentRequestTimeoutSeconds);
            Assert.AreEqual(expectedRetryCount, configuration.AssignmentRequestRetryCount);
        }

        [Test]
        public void AcceptsAssignmentOnlyTransportOverride()
        {
            var transport = new StubTransport();

            var configuration = new FlagsConfiguration(assignmentRequestTransport: transport);

            Assert.AreSame(transport, configuration.AssignmentRequestTransport);
            Assert.AreEqual(0, configuration.AssignmentRequestTimeoutSeconds);
            Assert.AreEqual(0, configuration.AssignmentRequestRetryCount);
        }

        [Test]
        public void PreservesPublishedConstructorSignatures()
        {
            Assert.IsNotNull(typeof(FlagsConfiguration).GetConstructor(new[]
            {
                typeof(bool),
                typeof(bool),
                typeof(float),
                typeof(string),
                typeof(string),
                typeof(string),
            }));
            Assert.IsNotNull(typeof(FlagsConfiguration).GetConstructor(new[]
            {
                typeof(int),
                typeof(int),
                typeof(bool),
                typeof(bool),
                typeof(float),
                typeof(string),
                typeof(string),
                typeof(string),
            }));
            Assert.IsNotNull(typeof(FlagsConfiguration).GetConstructor(new[]
            {
                typeof(IAssignmentRequestTransport),
                typeof(bool),
                typeof(bool),
                typeof(float),
                typeof(string),
                typeof(string),
                typeof(string),
            }));
            Assert.AreEqual(3, typeof(FlagsConfiguration).GetConstructors().Length);
        }

        private sealed class StubTransport : IAssignmentRequestTransport
        {
            private readonly AssignmentResponse _response;

            public StubTransport(AssignmentResponse response = null)
            {
                _response = response ?? new AssignmentResponse(
                    AssignmentRequestResult.Success,
                    200);
            }

            public int SendCount { get; private set; }

            public Task<AssignmentResponse> SendAsync(
                AssignmentRequest request,
                CancellationToken cancellationToken = default)
            {
                SendCount++;
                return Task.FromResult(_response);
            }
        }
    }
}
