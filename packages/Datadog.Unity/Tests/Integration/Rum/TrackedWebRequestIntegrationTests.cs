// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class TrackedWebRequestIntegrationTests : IntegrationTestBase
    {
        private const string NonFirstPartyUrl = "https://httpbin.org/status/200";

        // httpbin echoes the query parameters back as response headers.
        private const string HeadersUrl =
            "https://httpbin.org/response-headers?ETag=%22poc-etag%22&X-Request-Id=response-abc&X-Not-Listed=ignored&Access-Control-Expose-Headers=ETag%2CX-Request-Id";

        [UnityTest]
        [Category("integration")]
        public IEnumerator FirstPartyWrappedRequestsAreTraced()
        {
            return RunTracingScenario(firstParty: true, direct: false, requestCount: 2);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator NonFirstPartyWrappedRequestIsNotTraced()
        {
            return RunTracingScenario(firstParty: false, direct: false);
        }

        // Only the Browser SDK auto-instruments direct UnityWebRequest calls.
        [UnityTest]
        [Category("integration")]
        [UnityPlatform(include = new[] { RuntimePlatform.WebGLPlayer })]
        public IEnumerator FirstPartyDirectBrowserRequestIsTraced()
        {
            return RunTracingScenario(firstParty: true, direct: true);
        }

        // The Browser SDK matches full URLs; native wrappers match the parsed host and port.
        [UnityTest]
        [Category("integration")]
        [UnityPlatform(include = new[] { RuntimePlatform.WebGLPlayer })]
        public IEnumerator FirstPartyHostInPathDoesNotTraceUnrelatedBrowserOrigin()
        {
            return RunTracingScenario(firstParty: false, direct: true);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator TrackedWebRequestHeadersScenario()
        {
            var mockServerHelper = new MockServerHelper();
            yield return mockServerHelper.Clear();

            var viewKey = StartScenarioView();
            using (var request = new DatadogTrackedWebRequest(HeadersUrl))
            {
                request.SetRequestHeader("Cache-Control", "no-cache");
                request.SetRequestHeader("X-Request-Id", "request-abc");
                request.SetRequestHeader("X-Api-Key", "secret");
                request.SetRequestHeader("X-Not-Listed", "ignored");
                yield return WaitForCompletion(request.SendWebRequest());
                Assert.AreEqual(UnityEngine.Networking.UnityWebRequest.Result.Success, request.result, request.error);
            }

            RumViewVisit visit = null;
            yield return CompleteScenario(
                mockServerHelper,
                viewKey,
                new[] { HeadersUrl },
                (receivedVisit, _) => visit = receivedVisit);
            var resource = visit.ResourceEvents.Single(r => r.Url == HeadersUrl);

            // Integration settings capture the default headers plus x-request-id, etag and x-api-key, which is
            // sensitive and must be dropped
            var requestHeaders = resource.RequestHeaders;
            Assert.IsNotNull(requestHeaders, "resource.request.headers is missing");
            Assert.AreEqual("no-cache", requestHeaders["cache-control"]?.Value<string>());
            Assert.AreEqual("request-abc", requestHeaders["x-request-id"]?.Value<string>());
            Assert.IsNull(requestHeaders["x-api-key"], "Sensitive request headers must not be captured");

            var responseHeaders = resource.ResponseHeaders;
            Assert.IsNotNull(responseHeaders, "resource.response.headers is missing");
            Assert.AreEqual("\"poc-etag\"", responseHeaders["etag"]?.Value<string>());
            Assert.AreEqual("response-abc", responseHeaders["x-request-id"]?.Value<string>());
            Assert.IsNotNull(responseHeaders["content-type"]);
            Assert.IsNull(responseHeaders["x-not-listed"]);
            foreach (var header in responseHeaders.Properties())
            {
                Assert.AreEqual(header.Name.ToLowerInvariant(), header.Name, "Header keys must be lowercased");
            }
        }

        private static IEnumerator RunTracingScenario(bool firstParty, bool direct, int requestCount = 1)
        {
            var mockServerHelper = new MockServerHelper();
            yield return mockServerHelper.Clear();
            var viewKey = StartScenarioView();
            var endpoint = new Uri(DatadogConfigurationOptions.Load().CustomEndpoint);
            var requests = CreateRequests(endpoint, viewKey, firstParty, direct, requestCount).ToList();
            foreach (var requestInfo in requests)
            {
                if (requestInfo.Direct)
                {
                    using var request = UnityEngine.Networking.UnityWebRequest.Get(requestInfo.Url);
                    yield return WaitForCompletion(request.SendWebRequest());
                    Assert.AreEqual(UnityEngine.Networking.UnityWebRequest.Result.Success, request.result, request.error);
                }
                else
                {
                    using var request = DatadogTrackedWebRequest.Get(requestInfo.Url);
                    yield return WaitForCompletion(request.SendWebRequest());
                    Assert.AreEqual(UnityEngine.Networking.UnityWebRequest.Result.Success, request.result, request.error);
                }
            }

            RumViewVisit visit = null;
            List<MockServerLog> recorded = null;
            yield return CompleteScenario(
                mockServerHelper,
                viewKey,
                requests.Select(request => request.Url),
                (receivedVisit, receivedLogs) =>
                {
                    visit = receivedVisit;
                    recorded = receivedLogs;
                });
            var sessionId = visit.ResourceEvents.First(resource => resource.Url == requests[0].Url).Session;

            foreach (var requestInfo in requests)
            {
                var url = requestInfo.Url;
                var resources = visit.ResourceEvents.Where(resource => resource.Url == url).ToList();
                Assert.AreEqual(1, resources.Count, $"Expected exactly one RUM resource for {url}");
                var resource = resources[0];
                Assert.AreEqual(sessionId, resource.Session);
                if (url == NonFirstPartyUrl)
                {
                    Assert.IsNull(resource.TraceId);
                    Assert.IsNull(resource.SpanId);
                    continue;
                }

                var recordedRequests = recorded.Where(log => log.Endpoint == new Uri(url).AbsolutePath)
                    .SelectMany(log => log.Requests).ToList();
                Assert.AreEqual(1, recordedRequests.Count, $"Expected exactly one HTTP request for {url}");
                AssertTracing(recordedRequests[0].Schemas.First().ParsedHeaders, resource, requestInfo.FirstParty);
            }
        }

        private static string StartScenarioView()
        {
            var sdk = DatadogSdk.Instance;
            sdk.SetTrackingConsent(TrackingConsent.Granted);
            var viewKey = $"TrackedWebRequest-{Guid.NewGuid():N}";
            sdk.Rum.StartView(viewKey, viewKey);
            return viewKey;
        }

        private static IEnumerator CompleteScenario(
            MockServerHelper mockServerHelper,
            string viewKey,
            IEnumerable<string> urls,
            Action<RumViewVisit, List<MockServerLog>> receive)
        {
            var sdk = DatadogSdk.Instance;
#if UNITY_WEBGL
            // Browser views close when a successor starts; StopView is a no-op.
            sdk.Rum.StartView($"{viewKey}-complete");
#else
            sdk.Rum.StopView(viewKey);
#endif
            RumViewVisit visit = null;
            List<MockServerLog> recorded = null;
            yield return mockServerHelper.PollRequests(TimeSpan.FromSeconds(30), logs =>
            {
                recorded = logs;
                var events = RumDecoderHelpers.RumEventsFromMockServer(logs);
                visit = RumDecoderHelpers.RumSessionsFromEvents(events).SelectMany(session => session.Visits)
                    .SingleOrDefault(candidate => candidate.Name == viewKey);

                // Earlier tests can still upload view updates after the server is reset.
                return visit != null && visit.ViewEvents.Any(view => !view.View.IsActive)
                    && urls.All(url => visit.ResourceEvents.Any(resource => resource.Url == url));
            });

            Assert.IsNotNull(visit, $"No RUM view received for {viewKey}");
            Assert.IsTrue(visit.ViewEvents.Any(view => !view.View.IsActive), "The scenario's RUM view did not close");
            receive(visit, recorded);
        }

        private static IEnumerable<(string Url, bool FirstParty, bool Direct)> CreateRequests(
            Uri endpoint, string viewKey, bool firstParty, bool direct, int requestCount)
        {
            if (!firstParty && !direct)
            {
                yield return (NonFirstPartyUrl, false, false);
                yield break;
            }

            var origin = firstParty ? endpoint : new UriBuilder(endpoint)
            {
                Host = endpoint.Host == "127.0.0.1" ? "localhost" : "127.0.0.1",
            }.Uri;
            for (var i = 0; i < requestCount; i++)
            {
                // A configured host in the path must not make an unrelated origin traceable.
                var path = firstParty ? $"/integration_get/{viewKey}/{i}"
                    : $"/integration_get/{viewKey}/unrelated.{endpoint.Authority}/{i}";
                yield return (new Uri(origin, path).AbsoluteUri, firstParty, direct);
            }
        }

        private static IEnumerator WaitForCompletion(UnityEngine.Networking.UnityWebRequestAsyncOperation operation)
        {
            // Keep the request alive until the wrapper's completion callback has recorded its resource.
            var completed = false;
            operation.completed += _ => completed = true;
            yield return operation;
            while (!completed)
            {
                yield return null;
            }
        }

        private static void AssertTracing(Dictionary<string, string> headers, RumResourceEventDecoder resource, bool firstParty)
        {
            var session = RumSessionContext.FromSessionId(resource.Session);
            Assert.IsNotNull(session, "Resources must have a valid RUM session ID");
            var options = DatadogConfigurationOptions.Load();
            var rate = options.TraceSampleRate;
            var injectAll = options.TraceContextInjection == TraceContextInjection.All;
            var sampler = new DeterministicSampler(session.SamplingSeed, options.SessionSampleRate).CombineWith(rate);
            var sampled = firstParty && sampler.Sample();
            var inject = firstParty && (sampled || injectAll);
            Assert.AreEqual(inject, headers.ContainsKey("traceparent"));
            Assert.AreEqual(inject, headers.ContainsKey("tracestate"));
            Assert.AreEqual(inject, headers.ContainsKey("x-datadog-trace-id"));
#if UNITY_WEBGL
            // TODO(RUM-13050): Support baggage on all platforms.
            // Baggage is injected by the Browser SDK; the native Unity request wrapper does not implement it.
            Assert.AreEqual(inject, headers.ContainsKey("baggage"));
            if (inject)
            {
                StringAssert.Contains($"session.id={resource.Session}", headers["baggage"]);
            }
#endif
            if (inject)
            {
                Assert.AreEqual("rum", headers["x-datadog-origin"]);
                Assert.AreEqual(sampled ? "1" : "0", headers["x-datadog-sampling-priority"]);
                var traceParent = headers["traceparent"].Split('-');
                Assert.AreEqual(4, traceParent.Length);
                Assert.AreEqual("00", traceParent[0]);
                Assert.AreEqual(sampled ? "01" : "00", traceParent[3]);
                Assert.AreEqual(ulong.Parse(headers["x-datadog-trace-id"]).ToString("x16"), traceParent[1].Substring(16));
                Assert.AreEqual(ulong.Parse(headers["x-datadog-parent-id"]).ToString("x16"), traceParent[2]);
            }

            if (sampled)
            {
                Assert.IsNotNull(resource.TraceId);
#if UNITY_WEBGL
                Assert.AreEqual(headers["x-datadog-trace-id"], resource.TraceId);
#else
                // Native resources use hexadecimal trace IDs; iOS omits leading zeros.
                Assert.AreEqual(headers["traceparent"].Split('-')[1].TrimStart('0'), resource.TraceId.TrimStart('0'));
#endif
                Assert.AreEqual(headers["x-datadog-parent-id"], resource.SpanId);
                Assert.AreEqual(sampler.RulePsr, (float)resource.rumEvent["_dd"]["rule_psr"]);
            }
            else
            {
                Assert.IsNull(resource.TraceId);
                Assert.IsNull(resource.SpanId);
            }
        }
    }
}
