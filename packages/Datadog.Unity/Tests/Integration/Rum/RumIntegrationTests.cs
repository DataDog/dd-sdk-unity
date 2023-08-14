// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
                var events = RumEventsFromMockServer(serverLog);
                var sessions = RumSessionsFromEvents(events);
                return sessions.Count >= 1 && sessions[0].Visits.Count >= 1;
            });

            yield return new WaitUntil(() => task.IsCompleted);
            var serverLog = task.Result;
            var sessions = RumSessionsFromEvents(RumEventsFromMockServer(serverLog));

            Assert.AreEqual(1, sessions.Count);

            var session = sessions.First();
            Assert.AreEqual(1, session.Visits.Count);

            var viewVisit = session.Visits.First();
            Assert.AreEqual("First Screen", viewVisit.Name);
        }

        private List<RumEventDecoder> RumEventsFromMockServer(List<MockServerLog> mockServerLogs)
        {
            var rumEvents = new List<RumEventDecoder>();
            foreach (var mockLog in mockServerLogs)
            {
                if (mockLog.Endpoint.Contains("/rum"))
                {
                    mockLog.Requests.ForEach((e) => e.Schemas.ForEach((schema) =>
                    {
                        var lines = schema.DecompressedData.Split("\n");
                        foreach (var line in lines)
                        {
                            var jsonRum = JObject.Parse(line);
                            var rumEvent = RumEventDecoder.fromJson(jsonRum);
                            if (rumEvent != null)
                            {
                                rumEvents.Add(rumEvent);
                            }
                        }
                    }));
                }
            }

            return rumEvents;
        }

        private List<RumSessionDecoder> RumSessionsFromEvents(List<RumEventDecoder> events)
        {
            var sessionMap = new Dictionary<string, List<RumEventDecoder>>();
            foreach (var rumEvent in events)
            {
                var session = rumEvent.Session;
                if (session == null)
                {
                    continue;
                }

                if (!sessionMap.ContainsKey(session))
                {
                    sessionMap.Add(session, new List<RumEventDecoder>());
                }

                sessionMap[session].Add(rumEvent);
            }

            var orderedSessions = sessionMap.Values.OrderBy(e => e.First().Date).ToList();

            return orderedSessions.Select(x => new RumSessionDecoder(x)).ToList();
        }
    }

    public class TestRumMonoBehavior : MonoBehaviour, IMonoBehaviourTest
    {
        public bool IsTestFinished { get; private set; }

        public void Awake()
        {
            IsTestFinished = false;

            DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);

            var rum = DatadogSdk.Instance.Rum;
            rum?.StartView("FirstScreen", name: "First Screen");
            rum?.StopView("FirstScreen");

            IsTestFinished = true;
        }
    }
}
