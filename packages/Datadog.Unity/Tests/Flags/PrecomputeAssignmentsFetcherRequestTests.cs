// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class PrecomputeAssignmentsFetcherRequestTests
    {
        [Test]
        public void CreatesImmutableRequestWithPreservedSettings()
        {
            var fetcher = new PrecomputeAssignmentsFetcher(
                endpointUrl: "https://example.com/assignments",
                clientToken: "client-token",
                applicationId: "application-id",
                env: "test",
                logger: null);

            var request = fetcher.CreateRequest("body");

            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual("https://example.com/assignments", request.Url);
            Assert.AreEqual("body", request.Body);
            Assert.AreEqual("application/vnd.api+json", request.Headers["Content-Type"]);
            Assert.AreEqual("client-token", request.Headers["dd-client-token"]);
            Assert.AreEqual("application-id", request.Headers["dd-application-id"]);
        }

        [Test]
        public void DefaultTransportCreatesFreshUnityWebRequests()
        {
            var request = new AssignmentRequest(
                "PATCH",
                "https://example.com/assignments",
                "body",
                new Dictionary<string, string> { ["x-test"] = "value" });

            using var first = UnityWebRequestAssignmentTransport.CreateUnityWebRequest(request);
            using var second = UnityWebRequestAssignmentTransport.CreateUnityWebRequest(request);

            Assert.AreNotSame(first, second);
            Assert.AreEqual("PATCH", first.method);
            Assert.AreEqual("value", first.GetRequestHeader("x-test"));
        }

        [Test]
        public void CustomTransportBypassesScalarPolicy()
        {
            var transport = new RecordingTransport(new AssignmentResponse(
                AssignmentRequestResult.Success,
                200,
                @"{""data"":{""attributes"":{""flags"":{}}}}"));
            var completionCount = 0;
            Dictionary<string, FlagAssignment> completionResult = null;
            var fetcher = new PrecomputeAssignmentsFetcher(
                endpointUrl: "https://example.com/assignments",
                clientToken: "client-token",
                applicationId: null,
                env: "test",
                logger: null,
                requestTimeoutSeconds: -1,
                requestRetryCount: 10,
                assignmentRequestTransport: transport);

            fetcher.Fetch(new FlagsEvaluationContext("user-123"), result =>
            {
                completionCount++;
                completionResult = result;
            });

            Assert.AreEqual(1, transport.SendCount);
            Assert.AreEqual(1, completionCount);
            Assert.IsNotNull(completionResult);
        }

        [Test]
        public void DoesNotRetryUnexpectedTransportExceptions()
        {
            var transport = new ThrowingTransport();
            var completionCount = 0;
            var fetcher = new PrecomputeAssignmentsFetcher(
                endpointUrl: "not a valid URL",
                clientToken: "client-token",
                applicationId: null,
                env: "test",
                logger: null,
                requestTimeoutSeconds: 1,
                requestRetryCount: 10,
                assignmentRequestTransport: transport);

            fetcher.Fetch(new FlagsEvaluationContext("user-123"), result =>
            {
                completionCount++;
                Assert.IsNull(result);
            });

            Assert.AreEqual(1, transport.SendCount);
            Assert.AreEqual(1, completionCount);
        }

        [Test]
        public void DoesNotParseNon2xxResponseReportedAsSuccessByCustomTransport()
        {
            var transport = new RecordingTransport(new AssignmentResponse(
                AssignmentRequestResult.Success,
                503,
                @"{""data"":{""attributes"":{""flags"":{}}}}"));
            var completionCount = 0;
            var fetcher = new PrecomputeAssignmentsFetcher(
                endpointUrl: "https://example.com/assignments",
                clientToken: "client-token",
                applicationId: null,
                env: "test",
                logger: null,
                assignmentRequestTransport: transport);

            fetcher.Fetch(new FlagsEvaluationContext("user-123"), result =>
            {
                completionCount++;
                Assert.IsNull(result);
            });

            Assert.AreEqual(1, transport.SendCount);
            Assert.AreEqual(1, completionCount);
        }

        private sealed class RecordingTransport : IAssignmentRequestTransport
        {
            private readonly AssignmentResponse _response;

            public RecordingTransport(AssignmentResponse response)
            {
                _response = response;
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

        private sealed class ThrowingTransport : IAssignmentRequestTransport
        {
            public int SendCount { get; private set; }

            public Task<AssignmentResponse> SendAsync(
                AssignmentRequest request,
                CancellationToken cancellationToken = default)
            {
                SendCount++;
                var completion = new TaskCompletionSource<AssignmentResponse>();
                completion.SetException(new InvalidOperationException("invalid request configuration"));
                return completion.Task;
            }
        }
    }
}
