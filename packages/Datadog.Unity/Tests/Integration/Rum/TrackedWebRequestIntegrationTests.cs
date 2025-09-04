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

            // Run our test script, collecting the requests received by the mock server
            // until we've recorded a complete RUM session
            yield return new MonoBehaviourTest<TestTrackedWebRequestMonoBehavior>();
            List<MockServerLog> serverLog = new();
            List<MockServerLog> testRequests = new();
            yield return mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (logs) =>
            {
                serverLog = logs;
                testRequests = serverLog.Where(r => r.Endpoint.Contains("integration")).ToList();
                var events = RumDecoderHelpers.RumEventsFromMockServer(serverLog);
                var sessions = RumDecoderHelpers.RumSessionsFromEvents(events);

                // Second view makes sure the first one has been closed
                return sessions.Count >= 1 && sessions[0].Visits.Count >= 4;
            });

            // We should have recorded 1 RUM session
            var sessions = RumDecoderHelpers.RumSessionsFromEvents(
                RumDecoderHelpers.RumEventsFromMockServer(serverLog));
            Assert.AreEqual(1, sessions.Count);
            var session = sessions.First();

            // That session should include a visit to the 'First Screen' View, in
            // addition to the View events recorded automatically on scene change:
            // filter out any visits to 'InitTestScene' that will appear when running
            // in-editor, as well as automatic view events from any scene loads that
            // occurred prior to the start of TestTrackedWebRequestMonoBehavior
            var visits = session.Visits
                .Where(visit => visit.Name != string.Empty && !visit.Name.Contains("InitTestScene"))
                .SkipWhile(visit => visit.Name != "First Screen")
                .ToArray();

            // We should be left with two visits to RUM Views: the first being
            // 'First Screen', manually registered via StartView in our test script, and
            // the second having occurred when we loaded back into EmptyScene
            Assert.AreEqual(2, visits.Length);
            var firstVisit = visits[0];

            // Within that first View, we should have a Resource recorded to our
            // non-first-party endpoint, and as such no trace headers should have been
            // injected in that request
            var getResource = firstVisit.ResourceEvents.FirstOrDefault(r => r.Url.Contains("httpbin"));
            Assert.IsNotNull(getResource);
            Assert.AreEqual("https://httpbin.org/status/200", getResource.Url);
            Assert.IsNull(getResource.TraceId);
            Assert.IsNull(getResource.SpanId);

            // Examine the headers that were set on the first request received by the
            // mock server during the execution of our test script
            var testRequestEndpoint = testRequests.First();
            var testRequest = testRequestEndpoint.Requests.First();
            var schema = testRequest.Schemas.First();
            var headers = schema.ParsedHeaders;

            // This is mostly just checking that the headers exist. We could make this test more thorough
            // by decoding the trace and span ids and checking the values match the resource event.
            // For now, we'll only check the SpanId and assume unit testing covers the rest.
            Assert.AreEqual("rum", headers["x-datadog-origin"]);
            Assert.AreEqual("1", headers["x-datadog-sampling-priority"]);
            Assert.IsNotNull(headers["traceparent"]);

            // Without our 'First Screen' view, we should also have a Resource recorded
            // for the request to our first-party endpoint, and it _should_ have
            // injected trace context headers
            var getFirstPartyResource = firstVisit.ResourceEvents.FirstOrDefault(r => r.Url.Contains("integration_get"));
            Assert.IsNotNull(getFirstPartyResource);
            Assert.IsNotNull(getFirstPartyResource.TraceId);
            Assert.AreEqual(getFirstPartyResource.SpanId, headers["x-datadog-parent-id"]);
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

            // Make a tracked web request, not first party
            var getRequest = new DatadogTrackedWebRequest("https://httpbin.org/status/200");
            yield return getRequest.SendWebRequest();

            if (getRequest.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                Debug.Log($"Web request failed: {getRequest.error}");
            }

            // Make a tracked web request, first party. This must be configured in the settings as first party --
            // see `scripts/dev_setup.py` which will set the proper first party hosts
            var datadogSettings = DatadogConfigurationOptions.Load();
            var endpoint = datadogSettings.CustomEndpoint;
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
