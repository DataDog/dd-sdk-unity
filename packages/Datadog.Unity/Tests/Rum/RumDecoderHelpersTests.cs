// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Tests.Integration;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Datadog.Unity.Rum.Tests
{
    public class RumDecoderHelpersTests
    {
        private static JObject View(string session = "session", string id = "view", int version = 1)
        {
            return new JObject
            {
                ["type"] = "view",
                ["date"] = 100,
                ["_dd"] = new JObject { ["document_version"] = version },
                ["application"] = new JObject { ["id"] = "application" },
                ["session"] = new JObject { ["id"] = session },
                ["view"] = new JObject
                {
                    ["id"] = id, ["name"] = "Screen", ["is_active"] = true,
                    ["resource"] = new JObject { ["count"] = 0 },
                },
                ["context"] = new JObject { ["stage"] = 1, ["removed"] = true },
                ["ddtags"] = "version:1.123",
                ["usr"] = new JObject { ["id"] = "old-user" },
                ["account"] = new JObject { ["id"] = "old-account" },
            };
        }

        private static JObject Update(string session = "session", string id = "view", int version = 2)
        {
            return new JObject
            {
                ["type"] = "view_update",
                ["date"] = 100,
                ["_dd"] = new JObject { ["document_version"] = version },
                ["application"] = new JObject { ["id"] = "application" },
                ["session"] = new JObject { ["id"] = session },
                ["view"] = new JObject { ["id"] = id, ["is_active"] = false },
            };
        }

        private static List<RumEventDecoder> Decode(params JObject[] events)
        {
            return RumDecoderHelpers.RumEventsFromMockServer(new List<MockServerLog>
            {
                new()
                {
                    Endpoint = "/api/v2/rum",
                    Requests = new List<MockServerRequest>
                    {
                        new()
                        {
                            Schemas = new List<MockServerSchema>
                            {
                                new() { Data = string.Join("\n", events.Select(value => value.ToString(Formatting.None))) },
                            },
                        },
                    },
                },
            });
        }

        [Test]
        public void ViewUpdateClosesViewAndPreservesUnchangedFields()
        {
            var baseline = View();
            var update = Update();
            update["view"]["resource"] = new JObject { ["count"] = 1 };

            var events = Decode(baseline, update).Cast<RumViewEventDecoder>().ToList();

            Assert.AreEqual(2, events.Count);
            Assert.IsTrue(events[0].View.IsActive);
            Assert.IsFalse(events[1].View.IsActive);
            Assert.AreEqual("Screen", events[1].View.Name);
            Assert.AreEqual(1, events[1].View.ResourceCount);
            Assert.AreEqual(1, events[1].Attributes["stage"].Value<int>());
            Assert.AreEqual("1.123", events[1].ddtags["version"]);
            Assert.IsTrue(baseline["view"]["is_active"].Value<bool>());
            Assert.AreEqual("view_update", update.Value<string>("type"));
        }

        [TestCase("context")]
        [TestCase("feature_flags")]
        public void ChangedAttributeMapsReplacePreviousMaps(string field)
        {
            var baseline = View();
            baseline[field] = new JObject { ["removed"] = true };
            var update = Update();
            update[field] = new JObject { ["stage"] = 2 };

            var result = Decode(baseline, update).Last().rumEvent;

            Assert.IsNull(result[field]["removed"]);
            Assert.AreEqual(2, result[field]["stage"].Value<int>());
        }

        [TestCase("usr")]
        [TestCase("account")]
        public void MissingIdentityInUpdateClearsPreviousIdentity(string field)
        {
            Assert.IsNull(Decode(View(), Update()).Last().rumEvent[field]);
        }

        [Test]
        public void UpdatesAreMaterializedInDocumentVersionOrderBeforeSortingByDate()
        {
            var baseline = View();
            baseline["date"] = 300;
            var earlier = Update(version: 2);
            earlier["date"] = 200;
            earlier["view"]["is_active"] = true;
            earlier["view"]["resource"] = new JObject { ["count"] = 1 };
            var later = Update(version: 3);
            later["date"] = 100;

            var events = Decode(later, baseline, earlier).Cast<RumViewEventDecoder>().ToList();

            Assert.AreEqual(3, events.Count);
            CollectionAssert.AreEqual(new long[] { 100, 200, 300 }, events.Select(value => value.Date));
            var finalRevision = events.Single(value => value.rumEvent["_dd"]["document_version"].Value<int>() == 3);
            Assert.IsFalse(finalRevision.View.IsActive);
            Assert.AreEqual(1, finalRevision.View.ResourceCount);
        }

        [TestCase("resource")]
        [TestCase("action")]
        [TestCase("error")]
        [TestCase("telemetry")]
        [TestCase("long_task")]
        public void EventsWithoutDocumentVersionsKeepTheirDateOrder(string type)
        {
            var earlier = View();
            var later = View(id: "later-view");
            later["date"] = 300;
            var other = new JObject
            {
                ["type"] = type,
                ["date"] = 200,
                ["session"] = new JObject { ["id"] = "session" },
                ["view"] = new JObject { ["id"] = "view" },
            };

            var events = Decode(later, other, earlier);

            CollectionAssert.AreEqual(new long[] { 100, 200, 300 }, events.Select(value => value.Date));
            Assert.AreEqual(type, events[1].EventType);
        }

        [Test]
        public void LateResourceDoesNotReorderSessions()
        {
            var earlier = View(session: "earlier-session", id: "earlier-view");
            var later = View(session: "later-session", id: "later-view");
            later["date"] = 200;
            var resource = JObject.Parse(@"{ 'type': 'resource', 'date': 300, 'session': { 'id': 'earlier-session' }, 'view': { 'id': 'earlier-view' } }");

            var sessions = RumDecoderHelpers.RumSessionsFromEvents(Decode(later, resource, earlier));

            Assert.AreEqual(2, sessions.Count);
            Assert.AreEqual("earlier-view", sessions[0].Visits.Single().Id);
            Assert.AreEqual(1, sessions[0].Visits.Single().ResourceEvents.Count);
            Assert.AreEqual("later-view", sessions[1].Visits.Single().Id);
        }

        [Test]
        public void ViewsInDifferentSessionsHaveIndependentBaselines()
        {
            var other = View(session: "other-session");
            other["view"]["name"] = "Other screen";

            var events = Decode(View(), other, Update()).Cast<RumViewEventDecoder>().ToList();

            Assert.IsTrue(events.Single(value => value.Session == "other-session").View.IsActive);
            Assert.AreEqual("Screen", events.Last().View.Name);
            Assert.IsFalse(events.Last().View.IsActive);
        }

        [Test]
        public void UpdateWithoutBaselineIsIgnoredAfterServerReset()
        {
            var resource = JObject.Parse(@"{ 'type': 'resource', 'date': 200, 'session': { 'id': 'session' }, 'view': { 'id': 'view' } }");

            var events = Decode(Update(), resource);

            Assert.AreEqual(1, events.Count);
            Assert.IsInstanceOf<RumResourceEventDecoder>(events[0]);
        }

        [Test]
        public void LaterFullViewReplacesBaseline()
        {
            var full = View(version: 6);
            full["context"] = new JObject { ["stage"] = 3 };

            var events = Decode(View(), Update(), full, Update(version: 7));

            Assert.AreEqual(3, events.Last().Attributes["stage"].Value<int>());
        }
    }
}
