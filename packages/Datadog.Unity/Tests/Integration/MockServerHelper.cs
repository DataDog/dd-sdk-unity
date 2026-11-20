// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Datadog.Unity.Tests.Integration.Flags;
using Datadog.Unity.Tests.Integration.Logs;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine.Networking;

namespace Datadog.Unity.Tests.Integration
{
    public readonly struct MockServerResult<T>
    {
        internal MockServerResult(T result, MockServerSnapshot snapshot)
        {
            Result = result;
            Snapshot = snapshot;
        }

        public T Result { get; }

        public MockServerSnapshot Snapshot { get; }
    }

    public class MockServerHelper
    {
        public MockServerHelper(string endpoint = null)
        {
            Endpoint = (endpoint ?? DatadogConfigurationOptions.Load().CustomEndpoint).TrimEnd('/');
        }

        public string Endpoint { get; }

        public async Task Reset()
        {
            using var request = UnityWebRequest.Get($"{Endpoint}/reset");
            await AsyncTestHelpers.WaitForCompletion(request.SendWebRequest(), AsyncTestHelpers.DefaultTimeout);
            EnsureSuccess(request);
        }

        public async Task ConfigureResponse(string path, int status, string body, string contentType = "application/json")
        {
            var payload = JsonConvert.SerializeObject(new { path, status, body, content_type = contentType });
            using var request = new UnityWebRequest($"{Endpoint}/configure_response", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("Content-Type", "application/json");
            await AsyncTestHelpers.WaitForCompletion(request.SendWebRequest(), AsyncTestHelpers.DefaultTimeout);
            EnsureSuccess(request);
        }

        public async Task<MockServerSnapshot> ReadSnapshot(TimeSpan? timeout = null)
        {
            using var request = UnityWebRequest.Get($"{Endpoint}/inspect_requests/");
            await AsyncTestHelpers.WaitForCompletion(request.SendWebRequest(), timeout ?? AsyncTestHelpers.DefaultTimeout);
            EnsureSuccess(request);
            var content = request.downloadHandler.text;
            var contractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy(),
            };
            var logs = JsonConvert.DeserializeObject<List<MockServerLog>>(content, new JsonSerializerSettings
            {
                ContractResolver = contractResolver,
            })
                ?? throw new JsonSerializationException("Mock server returned a null snapshot.");
            return new MockServerSnapshot(logs);
        }

        public Task<MockServerResult<T>> WaitFor<T>(Func<MockServerSnapshot, T> select, Func<T, bool> until, TimeSpan? timeout = null)
        {
            return Poll(select, until, timeout ?? AsyncTestHelpers.DefaultTimeout, expectsResults: true);
        }

        public Task<MockServerResult<IReadOnlyList<T>>> ExpectNoneFor<T>(Func<MockServerSnapshot, IReadOnlyList<T>> select, TimeSpan duration)
        {
            return Poll(select, values => values.Count == 0, duration, expectsResults: false);
        }

        public Task<MockServerResult<MockServerRequest>> WaitForRequest(string path, string method = null)
        {
            return WaitFor(select: snapshot => snapshot.RequestsFor(path, method).FirstOrDefault(), until: request => request != null);
        }

        public Task<MockServerResult<RumViewVisit>> WaitForClosedView(string viewName, Func<RumViewVisit, bool> childrenReady = null)
        {
            return WaitFor(
                select: snapshot => snapshot.RumVisits.SingleOrDefault(visit => visit.Name == viewName),
                until: visit => visit != null && visit.ViewEvents.Any(view => !view.View.IsActive) && (childrenReady?.Invoke(visit) ?? true));
        }

        private static void EnsureSuccess(UnityWebRequest request)
        {
            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                throw new InvalidOperationException($"Mock server {request.method} {request.url} failed: HTTP {request.responseCode}, {request.error}");
            }
        }

        private async Task<MockServerResult<T>> Poll<T>(
            Func<MockServerSnapshot, T> select,
            Func<T, bool> until,
            TimeSpan duration,
            bool expectsResults)
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            var clock = Stopwatch.StartNew();
            MockServerSnapshot snapshot = null;
            TimeSpan remaining;
            while ((remaining = duration - clock.Elapsed) > TimeSpan.Zero)
            {
                snapshot = await ReadSnapshot(remaining);
                var value = select(snapshot);
                var ready = until(value);
                if (!expectsResults && !ready)
                {
                    throw new InvalidOperationException($"Unexpected items recorded by {Endpoint}: {snapshot}");
                }

                if (expectsResults && ready && clock.Elapsed < duration)
                {
                    return new MockServerResult<T>(value, snapshot);
                }

                await AsyncTestHelpers.Delay(TimeSpan.FromSeconds(Math.Max(0, Math.Min(2, (duration - clock.Elapsed).TotalSeconds))));
            }

            if (expectsResults)
            {
                throw new TimeoutException($"Mock server condition was not met after {clock.Elapsed.TotalSeconds:F1}s at {Endpoint}. Last snapshot: {snapshot}");
            }

            // Inspect after the window so processing an earlier snapshot cannot prove absence.
            snapshot = await ReadSnapshot();
            var finalValue = select(snapshot);
            if (!until(finalValue))
            {
                throw new InvalidOperationException($"Unexpected items recorded by {Endpoint}: {snapshot}");
            }

            return new MockServerResult<T>(finalValue, snapshot);
        }
    }

    public sealed class MockServerSnapshot
    {
        private readonly List<MockServerLog> _endpoints;
        private List<LogDecoder> _logs;
        private List<RumEventDecoder> _rumEvents;
        private List<RumSessionDecoder> _rumSessions;
        private List<RumViewVisit> _rumVisits;
        private List<ExposureEventDecoder> _exposures;
        private List<BatchedEvaluations> _evaluationBatches;
        private List<EvaluationRecord> _evaluationRecords;

        public MockServerSnapshot(List<MockServerLog> endpoints) => _endpoints = endpoints;

        public IReadOnlyList<MockServerLog> Endpoints => _endpoints;

        public IReadOnlyList<LogDecoder> Logs => _logs ??= LogDecoder.LogsFromMockServer(_endpoints);

        public IReadOnlyList<RumEventDecoder> RumEvents => _rumEvents ??= RumDecoderHelpers.RumEventsFromMockServer(_endpoints);

        public IReadOnlyList<RumSessionDecoder> RumSessions => _rumSessions ??= RumDecoderHelpers.RumSessionsFromEvents(RumEvents.ToList());

        public IReadOnlyList<RumViewVisit> RumVisits => _rumVisits ??= RumSessions.SelectMany(session => session.Visits).ToList();

        public IReadOnlyList<ExposureEventDecoder> Exposures => _exposures ??= ExposureEventDecoder.FromMockServer(_endpoints);

        public IReadOnlyList<BatchedEvaluations> EvaluationBatches => _evaluationBatches ??= EvaluationEventDecoder.FromMockServer(_endpoints);

        public IReadOnlyList<EvaluationRecord> EvaluationRecords => _evaluationRecords ??= EvaluationBatches.SelectMany(batch => batch.FlagEvaluations).ToList();

        public List<MockServerRequest> RequestsFor(string path, string method = null) => _endpoints
            .Where(endpoint => endpoint.Endpoint == path).SelectMany(endpoint => endpoint.Requests)
            .Where(request => method == null || string.Equals(request.Method, method, StringComparison.OrdinalIgnoreCase)).ToList();

        public override string ToString() => string.Join(", ", _endpoints.Select(endpoint => $"{endpoint.Endpoint}: {endpoint.Requests.Count} requests"));
    }

    public class MockServerLog
    {
        public string Endpoint { get; set; }

        public List<MockServerRequest> Requests { get; set; }
    }

    public class MockServerRequest
    {
        public string Method { get; set; }

        public string QueryString { get; set; }

        public NameValueCollection QueryParameters
        {
            get => HttpUtility.ParseQueryString(QueryString);
        }

        public List<MockServerSchema> Schemas { get; set; }

        public Dictionary<string, string> Headers => RawSchema.ParsedHeaders;

        public string BodyText => RawSchema.DecompressedData ?? RawSchema.Data;

        public JObject JsonBody => JObject.Parse(BodyText);

        private MockServerSchema RawSchema => Schemas?.Count == 1 ? Schemas[0]
            : throw new InvalidOperationException("Expected one raw schema on the recorded request.");
    }

    public class MockServerSchema
    {
        public List<string> Headers { get; set; }

        public Dictionary<string, string> ParsedHeaders
        {
            get
            {
                // HTTP headers are case-insensitive (RFC 7230). Flask normalizes header names to
                // title-case (e.g. "Dd-Api-Key"), so use OrdinalIgnoreCase to allow lookups
                // with any casing (e.g. "dd-api-key").
                var headerDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in Headers)
                {
                    var colonIndex = header.IndexOf(':');
                    var parts = (header[..colonIndex], header[(colonIndex + 1) ..].Trim());
                    headerDict[parts.Item1] = parts.Item2;
                }

                return headerDict;
            }
        }

        [CanBeNull]
        public string DecompressedData { get; set; }

        [CanBeNull]
        public string Data { get; set; }

        public List<T> ParseDecompressedJsonData<T>()
        {
            var dataString = DecompressedData ?? Data;
            try
            {
                return JsonConvert.DeserializeObject<List<T>>(dataString);
            }
            catch (JsonException)
            {
                // Try to split the data first by newlines
                return dataString.Split("\n")
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => JsonConvert.DeserializeObject<T>(line))
                    .ToList();
            }
        }
    }
}
