// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections;
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
        [UnityTest]
        [Category("integration")]
        public IEnumerator TrackedWebRequestScenario()
        {
            var mockServerHelper = new MockServerHelper();
            yield return mockServerHelper.Clear();

            var scenario = new MonoBehaviourTest<TestTrackedWebRequestMonoBehavior>();
            yield return scenario;

            RumViewVisit visit = null;
            MockServerRequest testRequest = null;
            yield return mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (logs) =>
            {
                testRequest = logs.Where(log => log.Endpoint == "/integration_get")
                    .SelectMany(log => log.Requests).FirstOrDefault();
                var events = RumDecoderHelpers.RumEventsFromMockServer(logs);
                var sessions = RumDecoderHelpers.RumSessionsFromEvents(events);
                visit = sessions.SelectMany(session => session.Visits)
                    .SingleOrDefault(candidate => candidate.Name == scenario.component.ViewKey);

                // Earlier tests can still upload view updates after the server is reset.
                // Wait only for this scenario's closed view and its two resources.
                return visit != null && testRequest != null
                    && visit.ViewEvents.Any(viewEvent => !viewEvent.View.IsActive)
                    && visit.ResourceEvents.Any(resource => resource.Url == TestTrackedWebRequestMonoBehavior.NonFirstPartyUrl)
                    && visit.ResourceEvents.Any(resource => resource.Url == scenario.component.FirstPartyUrl);
            });

            Assert.IsNotNull(visit, $"No RUM view received for {scenario.component.ViewKey}");
            Assert.IsTrue(visit.ViewEvents.Any(viewEvent => !viewEvent.View.IsActive),
                "The scenario's RUM view did not close");

            var getResource = visit.ResourceEvents.FirstOrDefault(r => r.Url == TestTrackedWebRequestMonoBehavior.NonFirstPartyUrl);
            Assert.IsNotNull(getResource);
            Assert.AreEqual("https://httpbin.org/status/200", getResource.Url);
            Assert.IsNull(getResource.TraceId);
            Assert.IsNull(getResource.SpanId);

            Assert.IsNotNull(testRequest, "The mock server did not receive the first-party request");
            var schema = testRequest.Schemas.First();
            var headers = schema.ParsedHeaders;

            // This is mostly just checking that the headers exist. We could make this test more thorough
            // by decoding the trace and span ids and checking the values match the resource event.
            // For now, we'll only check the SpanId and assume unit testing covers the rest.
            Assert.AreEqual("rum", headers["x-datadog-origin"]);
            Assert.AreEqual("1", headers["x-datadog-sampling-priority"]);
            Assert.IsTrue(headers.ContainsKey("traceparent"));

            var getFirstPartyResource = visit.ResourceEvents.FirstOrDefault(r => r.Url == scenario.component.FirstPartyUrl);
            Assert.IsNotNull(getFirstPartyResource);
            Assert.IsNotNull(getFirstPartyResource.TraceId);
            Assert.AreEqual(getFirstPartyResource.SpanId, headers["X-Datadog-Parent-Id"]);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator TrackedWebRequestHeadersScenario()
        {
            var mockServerHelper = new MockServerHelper();
            yield return mockServerHelper.Clear();

            var scenario = new MonoBehaviourTest<TestTrackedWebRequestHeadersMonoBehavior>();
            yield return scenario;

            RumViewVisit visit = null;
            yield return mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (logs) =>
            {
                var events = RumDecoderHelpers.RumEventsFromMockServer(logs);
                var sessions = RumDecoderHelpers.RumSessionsFromEvents(events);
                visit = sessions.SelectMany(session => session.Visits)
                    .SingleOrDefault(candidate => candidate.Name == scenario.component.ViewKey);

                return visit != null
                    && visit.ResourceEvents.Any(resource => resource.Url == TestTrackedWebRequestHeadersMonoBehavior.HeadersUrl);
            });

            Assert.IsNotNull(visit, $"No RUM view received for {scenario.component.ViewKey}");
            var resource = visit.ResourceEvents.First(r => r.Url == TestTrackedWebRequestHeadersMonoBehavior.HeadersUrl);

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
    }

    public class TestTrackedWebRequestHeadersMonoBehavior : MonoBehaviour, IMonoBehaviourTest
    {
        // httpbin echoes the query parameters back as response headers
        public const string HeadersUrl =
            "https://httpbin.org/response-headers?ETag=%22poc-etag%22&X-Request-Id=response-abc&X-Not-Listed=ignored";

        public string ViewKey { get; } = $"TrackedWebRequestHeadersScenario-{Guid.NewGuid():N}";

        public bool IsTestFinished { get; private set; }

        public void Awake()
        {
            IsTestFinished = false;
            DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);

            StartCoroutine(RunTest());
        }

        public IEnumerator RunTest()
        {
            var rum = DatadogSdk.Instance.Rum;
            rum?.StartView(ViewKey, name: ViewKey);

            var request = new DatadogTrackedWebRequest(HeadersUrl);
            request.SetRequestHeader("Cache-Control", "no-cache");
            request.SetRequestHeader("X-Request-Id", "request-abc");
            request.SetRequestHeader("X-Api-Key", "secret");
            request.SetRequestHeader("X-Not-Listed", "ignored");
            yield return request.SendWebRequest();

            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"Web request failed: {request.error}");
            }

            rum?.StopView(ViewKey);

            IsTestFinished = true;
        }
    }

    public class TestTrackedWebRequestMonoBehavior : MonoBehaviour, IMonoBehaviourTest
    {
        public const string NonFirstPartyUrl = "https://httpbin.org/status/200";

        public string ViewKey { get; } = $"TrackedWebRequestScenario-{Guid.NewGuid():N}";

        public string FirstPartyUrl { get; private set; }

        public bool IsTestFinished { get; private set; }

        public void Awake()
        {
            IsTestFinished = false;
            DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);

            StartCoroutine(RunTest());
        }

        public IEnumerator RunTest()
        {
            var rum = DatadogSdk.Instance.Rum;
            rum?.StartView(ViewKey, name: ViewKey);

            // Make a tracked web request, not first party
            var getRequest = new DatadogTrackedWebRequest(NonFirstPartyUrl);
            yield return getRequest.SendWebRequest();

            if (getRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"Web request failed: {getRequest.error}");
            }

            // The integration prebuild setup configures the mock server as a first-party host.
            var datadogSettings = DatadogConfigurationOptions.Load();
            var endpoint = datadogSettings.CustomEndpoint;
            FirstPartyUrl = $"{endpoint}/integration_get";
            var firstPartyGetRequest = new DatadogTrackedWebRequest(FirstPartyUrl);
            yield return firstPartyGetRequest.SendWebRequest();

            if (firstPartyGetRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"Web request failed: {firstPartyGetRequest.error}");
            }

            rum?.StopView(ViewKey);

            IsTestFinished = true;
        }
    }
}
