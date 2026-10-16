// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Globalization;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Validation and retry-delay policy for assignment requests.
    /// Kept separate from request execution so every edge case is deterministic and unit-testable.
    /// </summary>
    internal static class AssignmentRequestRetryPolicy
    {
        internal const int DisabledTimeoutSeconds = 0;
        // Task.Delay(TimeSpan) on supported Unity runtimes is bounded by an
        // Int32 millisecond timer. Keep whole seconds safely below that limit.
        internal const int MaxTimeoutSeconds = int.MaxValue / 1_000;
        internal const int DefaultRetryCount = 0;
        internal const int MaxRetryCount = 10;
        internal const int InitialBackoffMilliseconds = 100;
        internal const int MaxBackoffMilliseconds = 30_000;
        internal const int MaxRetryAfterMilliseconds = 30_000;

        internal static int NormalizeTimeoutSeconds(int timeoutSeconds)
        {
            return timeoutSeconds < 0
                ? DisabledTimeoutSeconds
                : Math.Min(MaxTimeoutSeconds, timeoutSeconds);
        }

        internal static int NormalizeRetryCount(int retryCount)
        {
            return Math.Max(0, Math.Min(MaxRetryCount, retryCount));
        }

        internal static bool TryGetRetryDelayMilliseconds(
            AssignmentRequestResult result,
            long httpCode,
            int attempt,
            int retryCount,
            string retryAfter,
            DateTimeOffset utcNow,
            double randomValue,
            out int retryDelayMilliseconds)
        {
            retryDelayMilliseconds = 0;

            if (attempt >= retryCount)
                return false;

            // HTTP status is authoritative even when a custom transport reports
            // an inconsistent result classification. Rate limiting is explicitly
            // not retried by this policy.
            if (httpCode == 429)
                return false;

            var retryableTransportError = result == AssignmentRequestResult.ConnectionError ||
                                          result == AssignmentRequestResult.DataProcessingError;
            var retryableHttpStatus = httpCode == 408 || (httpCode >= 500 && httpCode <= 599);

            if (!retryableTransportError && !retryableHttpStatus)
                return false;

            var retryAfterMilliseconds = 0L;
            if (httpCode == 503 &&
                TryParseRetryAfterMilliseconds(retryAfter, utcNow, out retryAfterMilliseconds) &&
                retryAfterMilliseconds > MaxRetryAfterMilliseconds)
            {
                return false;
            }

            var jitterMilliseconds = GetJitterDelayMilliseconds(attempt, randomValue);
            retryDelayMilliseconds = (int)Math.Min(
                int.MaxValue,
                retryAfterMilliseconds + jitterMilliseconds);
            return true;
        }

        internal static int GetJitterDelayMilliseconds(int attempt, double randomValue)
        {
            var maximum = (int)Math.Min(
                InitialBackoffMilliseconds * Math.Pow(2, Math.Max(0, attempt)),
                MaxBackoffMilliseconds);

            if (double.IsNaN(randomValue) || randomValue <= 0)
                return 0;
            if (randomValue >= 1)
                return maximum - 1;

            return (int)Math.Floor(randomValue * maximum);
        }

        private static bool TryParseRetryAfterMilliseconds(
            string retryAfter,
            DateTimeOffset utcNow,
            out long retryAfterMilliseconds)
        {
            retryAfterMilliseconds = 0;
            if (retryAfter == null)
                return false;

            var value = retryAfter.Trim();
            if (value.Length == 0)
                return false;

            var containsOnlyDigits = true;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    containsOnlyDigits = false;
                    break;
                }
            }

            if (containsOnlyDigits)
            {
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
                    seconds > long.MaxValue / 1_000)
                {
                    retryAfterMilliseconds = long.MaxValue;
                    return true;
                }

                retryAfterMilliseconds = seconds * 1_000;
                return true;
            }

            // Reject malformed numeric forms such as -1, 1.5, and 1e3 rather than
            // letting the permissive date parser interpret them as dates.
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return false;

            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var retryDate))
            {
                return false;
            }

            retryAfterMilliseconds = Math.Max(0, (long)(retryDate - utcNow).TotalMilliseconds);
            return true;
        }
    }
}
