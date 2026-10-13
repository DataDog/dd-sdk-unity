// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections;
using System.Linq;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
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
