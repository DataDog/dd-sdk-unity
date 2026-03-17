// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class TrackedWebRequestIntegrationTests
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator TrackedWebRequestScenario()
        {
            var mockServerHelper = new MockServerHelper();
            yield return mockServerHelper.Clear();

            yield return new MonoBehaviourTest<TestTrackedWebRequestMonoBehavior>();
            List<MockServerLog> serverLog = new();
            List<MockServerLog> testRequests = new();
            yield return mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (logs) =>
            {
                serverLog = logs;
                testRequests = serverLog.Where(r => r.Endpoint.Contains("integration")).ToList();
                var events = RumDecoderHelpers.RumEventsFromMockServer(serverLog);
                var sessions = RumDecoderHelpers.RumSessionsFromEvents(events);

                // Wait until we see the EmptyScene view that's loaded after "First Screen",
                // which guarantees "First Screen" has been closed and its events flushed.
                return sessions.Count >= 1 && sessions[0].Visits.Any(v => v.Name == "First Screen")
                    && sessions[0].Visits.Any(v => v.Name == "EmptyScene");
            });

            var sessions = RumDecoderHelpers.RumSessionsFromEvents(
                RumDecoderHelpers.RumEventsFromMockServer(serverLog));

            Assert.AreEqual(1, sessions.Count);

            var session = sessions.First();

            // Log all visit names to aid diagnosis of unexpected extra views.
            var allVisitNames = string.Join(", ", session.Visits.Select(v => $"\"{v.Name}\""));
            Debug.Log($"[TrackedWebRequest] All visits in session: [{allVisitNames}]");

            // Find the "First Screen" visit by name. Other views may appear in the session from
            // test framework scenes or views left open by preceding tests (e.g. EmptyScene from
            // RumIntegrationScenario), so we do not assert on the exact visit count.
            var firstVisit = session.Visits.FirstOrDefault(v => v.Name == "First Screen");
            Assert.IsNotNull(firstVisit, "Expected a 'First Screen' visit in the RUM session");
            var getResource = firstVisit.ResourceEvents.FirstOrDefault(r => r.Url.Contains("non_first_party"));
            Assert.IsNotNull(getResource, "Expected a non-first-party resource event");
            Assert.IsNull(getResource.TraceId);
            Assert.IsNull(getResource.SpanId);

            var testRequestEndpoint = testRequests.First();
            var testRequest = testRequestEndpoint.Requests.First();
            var schema = testRequest.Schemas.First();
            var headers = schema.ParsedHeaders;

            // This is mostly just checking that the headers exist. We could make this test more thorough
            // by decoding the trace and span ids and checking the values match the resource event.
            // For now, we'll only check the SpanId and assume unit testing covers the rest.
            Assert.AreEqual("rum", headers["X-Datadog-Origin"]);
            Assert.AreEqual("1", headers["X-Datadog-Sampling-Priority"]);
            Assert.IsNotNull(headers["Traceparent"]);

            var getFirstPartyResource = firstVisit.ResourceEvents.FirstOrDefault(r => r.Url.Contains("integration_get"));
            Assert.IsNotNull(getFirstPartyResource);
            Assert.IsNotNull(getFirstPartyResource.TraceId);
            Assert.AreEqual(getFirstPartyResource.SpanId, headers["X-Datadog-Parent-Id"]);
        }
    }

    public class TestTrackedWebRequestMonoBehavior : MonoBehaviour, IMonoBehaviourTest
    {
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
            rum?.StartView("FirstScreen", name: "First Screen");

            // Make a tracked web request that is NOT first-party. We use 10.0.2.2 (the Android
            // emulator's special alias for the host machine) with the mock server port. The mock
            // server binds to 0.0.0.0 so it accepts these connections, but the first-party hosts
            // list only includes the LAN IP, so the RUM SDK won't inject tracing headers here.
            var datadogSettings = DatadogConfigurationOptions.Load();
            var endpoint = datadogSettings.CustomEndpoint;
            var mockPort = new Uri(endpoint).Port;
            var nonFirstPartyUrl = $"http://10.0.2.2:{mockPort}/non_first_party_get";
            var getRequest = new DatadogTrackedWebRequest(nonFirstPartyUrl);
            yield return getRequest.SendWebRequest();

            if (getRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"Non-first-party web request failed: {getRequest.error}");
            }

            // Make a tracked web request, first party. This must be configured in the settings as first party --
            // see `scripts/dev_setup.py` which will set the proper first party hosts
            var firstPartyGetRequest = new DatadogTrackedWebRequest($"{endpoint}/integration_get");
            yield return firstPartyGetRequest.SendWebRequest();

            if (firstPartyGetRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"Web request failed: {firstPartyGetRequest.error}");
            }

            SceneManager.LoadScene("Scenes/EmptyScene");

            IsTestFinished = true;
        }
    }
}
