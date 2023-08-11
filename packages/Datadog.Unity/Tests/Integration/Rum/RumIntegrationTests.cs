// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class RumIntegrationTests
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator RumIntegrationScenario()
        {
            var mockServerHelper = new MockServerHelper();
            var resetTask = mockServerHelper.Clear();
            yield return new WaitUntil(() => resetTask.IsCompleted);

            yield return new MonoBehaviourTest<TestRumMonoBehavior>();
            var task = mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (serverLog) =>
            {
                var events = RumDecoderHelpers.RumEventsFromMockServer(serverLog);
                var sessions = RumDecoderHelpers.RumSessionsFromEvents(events);
                // Second view makes sure the first one has been closed
                return sessions.Count >= 1 && sessions[0].Visits.Count >= 2;
            });

            yield return new WaitUntil(() => task.IsCompleted);
            var serverLog = task.Result;
            var sessions = RumDecoderHelpers.RumSessionsFromEvents(
                RumDecoderHelpers.RumEventsFromMockServer(serverLog));

            Assert.AreEqual(1, sessions.Count);

            var session = sessions.First();
            Assert.AreEqual(2, session.Visits.Count);

            var viewVisit = session.Visits.First();
            Assert.AreEqual("First Screen", viewVisit.Name);

            Assert.AreEqual(1, viewVisit.ActionEvents.Count);
            var firstAction = viewVisit.ActionEvents[0];
            Assert.AreEqual("tap", firstAction.ActionType);
            Assert.AreEqual("Tapped Download", firstAction.ActionName);

            var contentReadyTiming = viewVisit.ViewEvents.Last().CustomTimings["content-ready"];
            Assert.IsNotNull(contentReadyTiming);
            // TODO: Timings are messed up because we don't capture time on the main thread.
            // Assert.GreaterOrEqual(50 * 1000 * 1000, contentReadyTiming);

            var firstInteractionTiming = viewVisit.ViewEvents.Last().CustomTimings["first-interaction"];
            Assert.IsNotNull(firstInteractionTiming);
            Assert.GreaterOrEqual(firstInteractionTiming, contentReadyTiming);

            Assert.AreEqual(1, viewVisit.ErrorEvents.Count);
            var errorEvent = viewVisit.ErrorEvents[0];
            Assert.AreEqual("System.Exception", errorEvent.ErrorType);
            Assert.AreEqual("Test Exception", errorEvent.Message);
            Assert.IsNotNull(errorEvent.Stack);
            Assert.AreEqual("custom", errorEvent.Source);
            Assert.AreEqual("first_call", errorEvent.Attributes["error_attribute"].Value<string>());
        }
    }

    public class TestRumMonoBehavior : MonoBehaviour, IMonoBehaviourTest
    {
        public bool IsTestFinished { get; private set; }

        public void Awake()
        {
            IsTestFinished = false;
            DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);

            StartCoroutine(RumTest());
        }

        public IEnumerator RumTest()
        {
            var rum = DatadogSdk.Instance.Rum;
            rum?.StartView("FirstScreen", name: "First Screen");

            yield return new WaitForSeconds(0.05f);
            rum?.AddTiming("content-ready");

            yield return new WaitForSeconds(0.5f);
            rum?.AddTiming("first-interaction");
            rum?.AddUserAction(RumUserActionType.Tap, "Tapped Download");

            try
            {
                throw new Exception("Test Exception");
            }
            catch(Exception e)
            {
                rum?.AddError(e, RumErrorSource.Custom, new()
                {
                    { "error_attribute", "first_call" },
                });
            }

            rum?.StartView("FinishedScreen", name: "Finished Screen");

            IsTestFinished = true;
        }
    }
}
