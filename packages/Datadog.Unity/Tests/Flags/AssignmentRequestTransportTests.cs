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
    public class AssignmentRequestTransportTests
    {
        private static readonly AssignmentRequest Request = new AssignmentRequest(
            "POST",
            "https://example.com/assignments",
            "body",
            new Dictionary<string, string> { ["x-test"] = "value" });

        [Test]
        public void TimeoutZeroReturnsInnerTransport()
        {
            var inner = new DelegateTransport((_, __) => Task.FromResult(Success()));

            Assert.AreSame(inner, inner.WithTimeout(0));
        }

        [Test]
        public void RetryZeroReturnsInnerTransport()
        {
            var inner = new DelegateTransport((_, __) => Task.FromResult(Success()));

            Assert.AreSame(inner, inner.WithRetry(0));
        }

        [Test]
        public void TimeoutRejectsNegativeValues()
        {
            var inner = new DelegateTransport((_, __) => Task.FromResult(Success()));

            Assert.Throws<ArgumentOutOfRangeException>(() => inner.WithTimeout(-1));
        }

        [TestCase(AssignmentRequestRetryPolicy.MaxTimeoutSeconds)]
        [TestCase(AssignmentRequestRetryPolicy.MaxTimeoutSeconds + 1)]
        [TestCase(int.MaxValue)]
        public async Task TimeoutAcceptsAndCapsUnityTimerBoundaryBeforeSending(int timeoutSeconds)
        {
            var inner = new DelegateTransport((_, __) => Task.FromResult(Success()));
            var transport = inner.WithTimeout(timeoutSeconds);

            var response = await transport.SendAsync(Request);

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(1, inner.SendCount);
        }

        [TestCase(-1)]
        [TestCase(11)]
        public void RetryRejectsValuesOutsidePublicBounds(int retries)
        {
            var inner = new DelegateTransport((_, __) => Task.FromResult(Success()));

            Assert.Throws<ArgumentOutOfRangeException>(() => inner.WithRetry(retries));
        }

        [Test]
        public void RequestAndResponseDefensivelyCopyHeaders()
        {
            var requestHeaders = new Dictionary<string, string> { ["x-test"] = "before" };
            var responseHeaders = new Dictionary<string, string> { ["retry-after"] = "1" };
            var request = new AssignmentRequest("PUT", "https://example.com", "body", requestHeaders);
            var response = new AssignmentResponse(
                AssignmentRequestResult.ProtocolError,
                503,
                headers: responseHeaders);

            requestHeaders["x-test"] = "after";
            responseHeaders["retry-after"] = "after";

            Assert.AreEqual("before", request.Headers["x-test"]);
            Assert.AreEqual("PUT", request.Method);
            Assert.AreEqual("1", response.GetHeader("Retry-After"));
        }

        [Test]
        public async Task TimeoutCancelsTheCompleteTransportOperation()
        {
            var cancellationObserved = new TaskCompletionSource<bool>();
            var inner = new DelegateTransport((_, cancellationToken) =>
            {
                var response = new TaskCompletionSource<AssignmentResponse>();
                cancellationToken.Register(() =>
                {
                    cancellationObserved.TrySetResult(true);
                    response.TrySetCanceled();
                });
                return response.Task;
            });
            var transport = new TimeoutAssignmentRequestTransport(
                inner,
                TimeSpan.FromMilliseconds(10));

            Assert.ThrowsAsync<TimeoutException>(async () => await transport.SendAsync(Request));
            Assert.IsTrue(await cancellationObserved.Task);
            Assert.AreEqual(1, inner.SendCount);
        }

        [Test]
        [Timeout(1_000)]
        public void TimeoutDoesNotWaitForNonCooperativeInnerTransport()
        {
            var cancellationObserved = false;
            var innerCompletion = new TaskCompletionSource<AssignmentResponse>();
            var inner = new DelegateTransport((_, cancellationToken) =>
            {
                cancellationToken.Register(() => cancellationObserved = true);
                return innerCompletion.Task;
            });
            var transport = new TimeoutAssignmentRequestTransport(
                inner,
                TimeSpan.FromMilliseconds(10));

            Assert.ThrowsAsync<TimeoutException>(async () => await transport.SendAsync(Request));
            Assert.IsTrue(cancellationObserved);
            Assert.IsFalse(innerCompletion.Task.IsCompleted);

            // The timeout continuation observes faults that arrive after the
            // caller has already received its TimeoutException.
            innerCompletion.TrySetException(new InvalidOperationException("late failure"));
        }

        [Test]
        public async Task RetryComposesOutsidePerAttemptTimeout()
        {
            var attempts = 0;
            var inner = new DelegateTransport((_, cancellationToken) =>
            {
                attempts++;
                if (attempts == 1)
                {
                    var pending = new TaskCompletionSource<AssignmentResponse>();
                    cancellationToken.Register(() => pending.TrySetCanceled());
                    return pending.Task;
                }
                return Task.FromResult(Success());
            });
            var timed = new TimeoutAssignmentRequestTransport(
                inner,
                TimeSpan.FromMilliseconds(10));
            var transport = AssignmentRequestTransports.BuildRetryTransport(
                timed,
                retries: 1,
                randomValue: () => 0,
                delay: (_, __) => Task.CompletedTask);

            var response = await transport.SendAsync(Request);

            Assert.AreEqual(AssignmentRequestResult.Success, response.Result);
            Assert.AreEqual(2, attempts);
        }

        [TestCase(408)]
        [TestCase(500)]
        [TestCase(599)]
        public async Task RetriesTransientProtocolResponses(int statusCode)
        {
            var attempts = 0;
            var inner = new DelegateTransport((_, __) => Task.FromResult(
                ++attempts == 1
                    ? new AssignmentResponse(AssignmentRequestResult.ProtocolError, statusCode)
                    : Success()));
            var transport = AssignmentRequestTransports.BuildRetryTransport(
                inner,
                retries: 1,
                randomValue: () => 0,
                delay: (_, __) => Task.CompletedTask);

            var response = await transport.SendAsync(Request);

            Assert.AreEqual(AssignmentRequestResult.Success, response.Result);
            Assert.AreEqual(2, attempts);
        }

        [Test]
        public async Task RetriesServerStatusReportedAsSuccessByCustomTransport()
        {
            var attempts = 0;
            var inner = new DelegateTransport((_, __) => Task.FromResult(
                ++attempts == 1
                    ? new AssignmentResponse(AssignmentRequestResult.Success, 503)
                    : Success()));
            var transport = AssignmentRequestTransports.BuildRetryTransport(
                inner,
                retries: 1,
                randomValue: () => 0,
                delay: (_, __) => Task.CompletedTask);

            var response = await transport.SendAsync(Request);

            Assert.AreEqual(200, response.StatusCode);
            Assert.AreEqual(2, attempts);
        }

        [Test]
        public async Task DoesNotRetryRateLimitedResponses()
        {
            var inner = new DelegateTransport((_, __) => Task.FromResult(
                new AssignmentResponse(AssignmentRequestResult.ProtocolError, 429)));
            var transport = AssignmentRequestTransports.BuildRetryTransport(
                inner,
                retries: 3,
                randomValue: () => 0,
                delay: (_, __) => Task.CompletedTask);

            var response = await transport.SendAsync(Request);

            Assert.AreEqual(429, response.StatusCode);
            Assert.AreEqual(1, inner.SendCount);
        }

        [Test]
        public async Task DoesNotRetryRateLimitReportedAsTransportError()
        {
            var inner = new DelegateTransport((_, __) => Task.FromResult(
                new AssignmentResponse(AssignmentRequestResult.ConnectionError, 429)));
            var transport = AssignmentRequestTransports.BuildRetryTransport(
                inner,
                retries: 3,
                randomValue: () => 0,
                delay: (_, __) => Task.CompletedTask);

            var response = await transport.SendAsync(Request);

            Assert.AreEqual(429, response.StatusCode);
            Assert.AreEqual(1, inner.SendCount);
        }

        [Test]
        public async Task SerializesJitterSourceAcrossConcurrentRequests()
        {
            var activeCalls = 0;
            var maximumConcurrentCalls = 0;
            var inner = new DelegateTransport((_, __) => Task.FromResult(
                new AssignmentResponse(AssignmentRequestResult.Success, 500)));
            var transport = AssignmentRequestTransports.BuildRetryTransport(
                inner,
                retries: 1,
                randomValue: () =>
                {
                    var active = Interlocked.Increment(ref activeCalls);
                    var observedMaximum = maximumConcurrentCalls;
                    while (active > observedMaximum)
                    {
                        var previous = Interlocked.CompareExchange(
                            ref maximumConcurrentCalls,
                            active,
                            observedMaximum);
                        if (previous == observedMaximum)
                            break;
                        observedMaximum = previous;
                    }

                    Thread.Sleep(5);
                    Interlocked.Decrement(ref activeCalls);
                    return 0;
                },
                delay: (_, __) => Task.CompletedTask);
            var operations = new Task<AssignmentResponse>[32];
            for (var i = 0; i < operations.Length; i++)
            {
                operations[i] = Task.Run(async () => await transport.SendAsync(Request));
            }

            await Task.WhenAll(operations);

            Assert.AreEqual(1, maximumConcurrentCalls);
        }

        [Test]
        public void CallerCancellationInterruptsRetryDelay()
        {
            using var cancellation = new CancellationTokenSource();
            var delayStarted = new TaskCompletionSource<bool>();
            var inner = new DelegateTransport((_, __) => Task.FromResult(
                new AssignmentResponse(AssignmentRequestResult.ProtocolError, 500)));
            var transport = AssignmentRequestTransports.BuildRetryTransport(
                inner,
                retries: 1,
                randomValue: () => 0,
                delay: (_, cancellationToken) =>
                {
                    delayStarted.TrySetResult(true);
                    return Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
                });

            var operation = transport.SendAsync(Request, cancellation.Token);
            Assert.IsTrue(delayStarted.Task.IsCompleted);
            cancellation.Cancel();

            Assert.ThrowsAsync<TaskCanceledException>(async () => await operation);
            Assert.AreEqual(1, inner.SendCount);
        }

        [Test]
        public void UnexpectedTransportExceptionsAreNotRetried()
        {
            var inner = new DelegateTransport((_, __) =>
            {
                var failure = new TaskCompletionSource<AssignmentResponse>();
                failure.SetException(new InvalidOperationException("bad transport"));
                return failure.Task;
            });
            var transport = AssignmentRequestTransports.BuildRetryTransport(
                inner,
                retries: 3,
                randomValue: () => 0,
                delay: (_, __) => Task.CompletedTask);

            Assert.ThrowsAsync<InvalidOperationException>(async () => await transport.SendAsync(Request));
            Assert.AreEqual(1, inner.SendCount);
        }

        private static AssignmentResponse Success()
        {
            return new AssignmentResponse(AssignmentRequestResult.Success, 200, "{}");
        }

        private sealed class DelegateTransport : IAssignmentRequestTransport
        {
            private readonly Func<AssignmentRequest, CancellationToken, Task<AssignmentResponse>> _send;

            public DelegateTransport(
                Func<AssignmentRequest, CancellationToken, Task<AssignmentResponse>> send)
            {
                _send = send;
            }

            public int SendCount { get; private set; }

            public Task<AssignmentResponse> SendAsync(
                AssignmentRequest request,
                CancellationToken cancellationToken = default)
            {
                SendCount++;
                return _send(request, cancellationToken);
            }
        }
    }
}
