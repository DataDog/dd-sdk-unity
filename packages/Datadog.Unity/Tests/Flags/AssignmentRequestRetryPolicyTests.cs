// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class AssignmentRequestRetryPolicyTests
    {
        private static readonly DateTimeOffset RetryDate =
            new DateTimeOffset(2026, 8, 28, 16, 0, 0, TimeSpan.Zero);

        [TestCase(AssignmentRequestResult.ConnectionError, 0, true)]
        [TestCase(AssignmentRequestResult.DataProcessingError, 0, true)]
        [TestCase(AssignmentRequestResult.ProtocolError, 408, true)]
        [TestCase(AssignmentRequestResult.ProtocolError, 500, true)]
        [TestCase(AssignmentRequestResult.ProtocolError, 599, true)]
        [TestCase(AssignmentRequestResult.Success, 408, true)]
        [TestCase(AssignmentRequestResult.Success, 503, true)]
        [TestCase(AssignmentRequestResult.ConnectionError, 429, false)]
        [TestCase(AssignmentRequestResult.ProtocolError, 400, false)]
        [TestCase(AssignmentRequestResult.ProtocolError, 429, false)]
        [TestCase(AssignmentRequestResult.ProtocolError, 600, false)]
        [TestCase(AssignmentRequestResult.Success, 200, false)]
        public void SelectsOnlyTransientFailuresForRetry(
            AssignmentRequestResult result,
            long statusCode,
            bool expected)
        {
            var shouldRetry = TryGetRetryDelay(
                result,
                statusCode,
                attempt: 0,
                retryCount: 1,
                retryAfter: null,
                out _);

            Assert.AreEqual(expected, shouldRetry);
        }

        [TestCase(0, 0, false)]
        [TestCase(1, 1, false)]
        [TestCase(9, 10, true)]
        public void RespectsRetryBudget(int attempt, int retryCount, bool expected)
        {
            var shouldRetry = TryGetRetryDelay(
                AssignmentRequestResult.ConnectionError,
                statusCode: 0,
                attempt,
                retryCount,
                retryAfter: null,
                out _);

            Assert.AreEqual(expected, shouldRetry);
        }

        [Test]
        public void UsesRetryAfterSecondsAsFloorBeforeJitter()
        {
            var shouldRetry = TryGetRetryDelay(
                AssignmentRequestResult.ProtocolError,
                statusCode: 503,
                attempt: 0,
                retryCount: 1,
                retryAfter: "1",
                out var delayMilliseconds);

            Assert.IsTrue(shouldRetry);
            Assert.AreEqual(1_050, delayMilliseconds);
        }

        [Test]
        public void UsesRetryAfterDateAsFloorBeforeJitter()
        {
            var shouldRetry = TryGetRetryDelay(
                AssignmentRequestResult.ProtocolError,
                statusCode: 503,
                attempt: 0,
                retryCount: 1,
                retryAfter: "Fri, 28 Aug 2026 16:00:01 GMT",
                out var delayMilliseconds);

            Assert.IsTrue(shouldRetry);
            Assert.AreEqual(1_050, delayMilliseconds);
        }

        [TestCase("0")]
        [TestCase("Fri, 28 Aug 2026 15:59:59 GMT")]
        public void UsesJitterWhenRetryAfterIsImmediateOrPast(string retryAfter)
        {
            var shouldRetry = TryGetRetryDelay(
                AssignmentRequestResult.ProtocolError,
                statusCode: 503,
                attempt: 0,
                retryCount: 1,
                retryAfter,
                out var delayMilliseconds);

            Assert.IsTrue(shouldRetry);
            Assert.AreEqual(50, delayMilliseconds);
        }

        [TestCase("31")]
        [TestCase("999999999999999999999999999999999999999")]
        [TestCase("Fri, 28 Aug 2026 16:00:31 GMT")]
        public void DoesNotRetryWhenRetryAfterExceedsMaximum(string retryAfter)
        {
            Assert.IsFalse(TryGetRetryDelay(
                AssignmentRequestResult.ProtocolError,
                statusCode: 503,
                attempt: 0,
                retryCount: 1,
                retryAfter,
                out _));
        }

        [Test]
        public void RetriesAtMaximumRetryAfter()
        {
            var shouldRetry = TryGetRetryDelay(
                AssignmentRequestResult.ProtocolError,
                statusCode: 503,
                attempt: 0,
                retryCount: 1,
                retryAfter: "30",
                out var delayMilliseconds);

            Assert.IsTrue(shouldRetry);
            Assert.AreEqual(30_050, delayMilliseconds);
        }

        [TestCase(500, "10")]
        [TestCase(503, "")]
        [TestCase(503, "1.5")]
        [TestCase(503, "1e3")]
        [TestCase(503, "-1")]
        [TestCase(503, "not-a-date")]
        public void IgnoresInapplicableOrMalformedRetryAfter(long statusCode, string retryAfter)
        {
            var shouldRetry = TryGetRetryDelay(
                AssignmentRequestResult.ProtocolError,
                statusCode,
                attempt: 0,
                retryCount: 1,
                retryAfter,
                out var delayMilliseconds);

            Assert.IsTrue(shouldRetry);
            Assert.AreEqual(50, delayMilliseconds);
        }

        [TestCase(0, 50)]
        [TestCase(1, 100)]
        [TestCase(9, 15_000)]
        public void UsesBoundedExponentialFullJitter(int attempt, int expectedDelayMilliseconds)
        {
            Assert.AreEqual(
                expectedDelayMilliseconds,
                AssignmentRequestRetryPolicy.GetJitterDelayMilliseconds(attempt, randomValue: 0.5));
        }

        private static bool TryGetRetryDelay(
            AssignmentRequestResult result,
            long statusCode,
            int attempt,
            int retryCount,
            string retryAfter,
            out int retryDelayMilliseconds)
        {
            return AssignmentRequestRetryPolicy.TryGetRetryDelayMilliseconds(
                result,
                statusCode,
                attempt,
                retryCount,
                retryAfter,
                RetryDate,
                randomValue: 0.5,
                out retryDelayMilliseconds);
        }
    }
}
