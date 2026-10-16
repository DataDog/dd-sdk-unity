// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Immutable assignment request passed to an <see cref="IAssignmentRequestTransport"/>.
    /// </summary>
    public sealed class AssignmentRequest
    {
        public AssignmentRequest(
            string method,
            string url,
            string body,
            IDictionary<string, string> headers)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Body = body ?? string.Empty;
            Headers = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(headers ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase));
        }

        public string Method { get; }

        public string Url { get; }

        public string Body { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }
    }

    /// <summary>
    /// Transport-level result of an assignment request.
    /// </summary>
    public enum AssignmentRequestResult
    {
        Success,
        ConnectionError,
        ProtocolError,
        DataProcessingError,
    }

    /// <summary>
    /// Immutable, fully buffered response returned by an assignment transport.
    /// Custom transports must represent retryable connection and data-processing failures with
    /// <see cref="AssignmentRequestResult.ConnectionError"/> or
    /// <see cref="AssignmentRequestResult.DataProcessingError"/>. HTTP status is validated
    /// independently: retry policy recognizes HTTP 408 and 5xx even if a custom transport reports
    /// an inconsistent result, and assignment parsing still requires a successful 2xx response.
    /// Unexpected implementation exceptions may be thrown and are not retried by
    /// <see cref="AssignmentRequestTransports.WithRetry"/>.
    /// </summary>
    public sealed class AssignmentResponse
    {
        public AssignmentResponse(
            AssignmentRequestResult result,
            long statusCode,
            string body = null,
            IDictionary<string, string> headers = null)
        {
            Result = result;
            StatusCode = statusCode;
            Body = body ?? string.Empty;
            Headers = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(headers ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase));
        }

        public AssignmentRequestResult Result { get; }

        public long StatusCode { get; }

        public string Body { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public string GetHeader(string name)
        {
            return name != null && Headers.TryGetValue(name, out var value) ? value : null;
        }
    }

    /// <summary>
    /// Sends fully formed assignment requests and returns fully buffered responses.
    /// Implementations own all native request objects they create and must release them before
    /// completing the returned task. Callers retain ownership of the transport itself. Expected
    /// connection and response-processing failures must be returned as an
    /// <see cref="AssignmentResponse"/> with the corresponding <see cref="AssignmentRequestResult"/>;
    /// thrown unexpected exceptions are not retried by the retry decorator. A configured transport
    /// can be shared by multiple flags clients, so implementations must support concurrent calls
    /// and promptly observe the supplied cancellation token to release in-flight resources.
    /// </summary>
    public interface IAssignmentRequestTransport
    {
        Task<AssignmentResponse> SendAsync(
            AssignmentRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Assignment-only transport building blocks. The default transport uses
    /// <see cref="UnityWebRequest"/> while keeping its lifecycle private to the SDK.
    /// </summary>
    public static class AssignmentRequestTransports
    {
        private static readonly IAssignmentRequestTransport DefaultTransport =
            new UnityWebRequestAssignmentTransport();

        /// <summary>
        /// SDK-owned UnityWebRequest transport. Each call creates and disposes a fresh request.
        /// </summary>
        public static IAssignmentRequestTransport Default => DefaultTransport;

        /// <summary>
        /// Adds a timeout to each complete assignment request, including response-body download.
        /// At the deadline, cancellation is requested from the inner transport and the timeout
        /// completes promptly; the inner transport's cancellation contract is responsible for
        /// promptly releasing its resources. Values above 2,147,483 seconds are capped for
        /// compatibility across Unity runtimes. Zero returns <paramref name="inner"/> unchanged.
        /// </summary>
        public static IAssignmentRequestTransport WithTimeout(
            this IAssignmentRequestTransport inner,
            int timeoutSeconds)
        {
            if (inner == null)
                throw new ArgumentNullException(nameof(inner));
            if (timeoutSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must not be negative.");
            return timeoutSeconds == 0
                ? inner
                : new TimeoutAssignmentRequestTransport(
                    inner,
                    AssignmentRequestRetryPolicy.NormalizeTimeoutSeconds(timeoutSeconds));
        }

        /// <summary>
        /// Adds retries after transient assignment request failures.
        /// </summary>
        public static IAssignmentRequestTransport WithRetry(
            this IAssignmentRequestTransport inner,
            int retries)
        {
            return BuildRetryTransport(inner, retries);
        }

        internal static IAssignmentRequestTransport BuildRetryTransport(
            IAssignmentRequestTransport inner,
            int retries,
            Func<double> randomValue = null,
            Func<DateTimeOffset> utcNow = null,
            Func<int, CancellationToken, Task> delay = null)
        {
            if (inner == null)
                throw new ArgumentNullException(nameof(inner));
            if (retries < 0 || retries > AssignmentRequestRetryPolicy.MaxRetryCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retries),
                    $"Retries must be in [0, {AssignmentRequestRetryPolicy.MaxRetryCount}].");
            }

            if (retries == 0)
                return inner;

            var random = new Random();
            var randomLock = new object();
            var nextRandomValue = randomValue ?? new Func<double>(random.NextDouble);
            return new RetryAssignmentRequestTransport(
                inner,
                retries,
                () =>
                {
                    // A composed transport may be shared by concurrent clients.
                    // System.Random and injected test sources are not required to
                    // be thread-safe, so serialize access at the decorator edge.
                    lock (randomLock)
                    {
                        return nextRandomValue();
                    }
                },
                utcNow ?? new Func<DateTimeOffset>(() => DateTimeOffset.UtcNow),
                delay ?? ((milliseconds, token) => Task.Delay(milliseconds, token)));
        }
    }

    internal sealed class UnityWebRequestAssignmentTransport : IAssignmentRequestTransport
    {
        public Task<AssignmentResponse> SendAsync(
            AssignmentRequest assignmentRequest,
            CancellationToken cancellationToken = default)
        {
            if (assignmentRequest == null)
                throw new ArgumentNullException(nameof(assignmentRequest));

            var completion = new TaskCompletionSource<AssignmentResponse>();
            if (cancellationToken.IsCancellationRequested)
            {
                completion.SetCanceled();
                return completion.Task;
            }

            UnityWebRequest webRequest = null;
            CancellationTokenRegistration cancellationRegistration = default;
            try
            {
                webRequest = CreateUnityWebRequest(assignmentRequest);
                var mainThreadContext = SynchronizationContext.Current;
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationRegistration = cancellationToken.Register(() =>
                    {
                        void Abort()
                        {
                            try
                            {
                                webRequest.Abort();
                            }
                            catch (ObjectDisposedException)
                            {
                                // Completion won the race and already released the native request.
                            }
                        }

                        if (mainThreadContext != null)
                        {
                            // Always post, even when cancellation originates on
                            // the main thread. This lets the cancellation callback
                            // return before Abort can complete the operation and
                            // dispose its own CancellationTokenRegistration.
                            mainThreadContext.Post(_ => Abort(), null);
                        }
                        else
                        {
                            Abort();
                        }
                    });
                }

                var operation = webRequest.SendWebRequest();
                operation.completed += _ =>
                {
                    AssignmentResponse response = null;
                    Exception failure = null;
                    var cancelled = cancellationToken.IsCancellationRequested;
                    try
                    {
                        if (!cancelled)
                            response = CreateResponse(webRequest);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        // The public response contains no native Unity objects.
                        // Release the complete attempt before completing the task
                        // so continuations cannot observe ambiguous ownership.
                        cancellationRegistration.Dispose();
                        webRequest.Dispose();
                    }

                    if (cancelled)
                        completion.TrySetCanceled();
                    else if (failure != null)
                        completion.TrySetException(failure);
                    else
                        completion.TrySetResult(response);
                };
            }
            catch (Exception exception)
            {
                cancellationRegistration.Dispose();
                webRequest?.Dispose();
                completion.TrySetException(exception);
            }

            return completion.Task;
        }

        internal static UnityWebRequest CreateUnityWebRequest(AssignmentRequest request)
        {
            var webRequest = new UnityWebRequest(request.Url, request.Method)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            foreach (var header in request.Headers)
            {
                webRequest.SetRequestHeader(header.Key, header.Value);
            }
            return webRequest;
        }

        private static AssignmentResponse CreateResponse(UnityWebRequest request)
        {
            return new AssignmentResponse(
                ToAssignmentResult(request.result),
                request.responseCode,
                request.downloadHandler?.text,
                request.GetResponseHeaders());
        }

        private static AssignmentRequestResult ToAssignmentResult(UnityWebRequest.Result result)
        {
            switch (result)
            {
                case UnityWebRequest.Result.Success:
                    return AssignmentRequestResult.Success;
                case UnityWebRequest.Result.ProtocolError:
                    return AssignmentRequestResult.ProtocolError;
                case UnityWebRequest.Result.DataProcessingError:
                    return AssignmentRequestResult.DataProcessingError;
                default:
                    return AssignmentRequestResult.ConnectionError;
            }
        }
    }

    internal sealed class TimeoutAssignmentRequestTransport : IAssignmentRequestTransport
    {
        private readonly IAssignmentRequestTransport _inner;
        private readonly TimeSpan _timeout;

        public TimeoutAssignmentRequestTransport(IAssignmentRequestTransport inner, int timeoutSeconds)
            : this(
                inner,
                TimeSpan.FromSeconds(
                    AssignmentRequestRetryPolicy.NormalizeTimeoutSeconds(timeoutSeconds)))
        {
        }

        internal TimeoutAssignmentRequestTransport(IAssignmentRequestTransport inner, TimeSpan timeout)
        {
            _inner = inner;
            _timeout = timeout;
        }

        public async Task<AssignmentResponse> SendAsync(
            AssignmentRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var timerCancellation = new CancellationTokenSource();
            var callerCancellation = new TaskCompletionSource<bool>();
            using var callerCancellationRegistration = cancellationToken.Register(
                () => callerCancellation.TrySetResult(true));
            var operation = _inner.SendAsync(request, requestCancellation.Token);
            var timeout = Task.Delay(_timeout, timerCancellation.Token);
            var completed = await Task.WhenAny(operation, timeout, callerCancellation.Task);

            if (completed == operation)
            {
                timerCancellation.Cancel();
                return await operation;
            }

            if (completed == callerCancellation.Task)
                timerCancellation.Cancel();
            requestCancellation.Cancel();
            ObserveFault(operation);
            if (completed == callerCancellation.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new TimeoutException($"The assignment request timed out after {_timeout.TotalMilliseconds} ms.");
        }

        private static void ObserveFault(Task operation)
        {
            operation.ContinueWith(
                task =>
                {
                    var ignored = task.Exception;
                },
                TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously);
        }
    }

    internal sealed class RetryAssignmentRequestTransport : IAssignmentRequestTransport
    {
        private readonly IAssignmentRequestTransport _inner;
        private readonly int _retries;
        private readonly Func<double> _randomValue;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<int, CancellationToken, Task> _delay;

        public RetryAssignmentRequestTransport(
            IAssignmentRequestTransport inner,
            int retries,
            Func<double> randomValue,
            Func<DateTimeOffset> utcNow,
            Func<int, CancellationToken, Task> delay)
        {
            _inner = inner;
            _retries = retries;
            _randomValue = randomValue;
            _utcNow = utcNow;
            _delay = delay;
        }

        public async Task<AssignmentResponse> SendAsync(
            AssignmentRequest request,
            CancellationToken cancellationToken = default)
        {
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AssignmentResponse response;
                try
                {
                    response = await _inner.SendAsync(request, cancellationToken);
                }
                catch (TimeoutException) when (attempt < _retries)
                {
                    await _delay(
                        AssignmentRequestRetryPolicy.GetJitterDelayMilliseconds(attempt, _randomValue()),
                        cancellationToken);
                    continue;
                }

                if (response == null)
                    throw new InvalidOperationException("Assignment transport returned a null response.");

                if (!AssignmentRequestRetryPolicy.TryGetRetryDelayMilliseconds(
                        response.Result,
                        response.StatusCode,
                        attempt,
                        _retries,
                        response.GetHeader("Retry-After"),
                        _utcNow(),
                        _randomValue(),
                        out var retryDelayMilliseconds))
                {
                    return response;
                }

                await _delay(retryDelayMilliseconds, cancellationToken);
            }
        }
    }
}
