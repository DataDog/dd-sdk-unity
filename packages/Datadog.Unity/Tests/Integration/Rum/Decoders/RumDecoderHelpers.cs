// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public static class RumDecoderHelpers
    {
        public static List<RumEventDecoder> RumEventsFromMockServer(List<MockServerLog> mockServerLogs)
        {
            var jsonEvents = new List<JObject>();
            foreach (var mockLog in mockServerLogs)
            {
                if (mockLog.Endpoint.Contains("/rum"))
                {
                    mockLog.Requests.ForEach((e) => e.Schemas.ForEach((schema) =>
                    {
                        var data = schema.DecompressedData ?? schema.Data;
                        var lines = data?.Split("\n");
                        if (lines == null || lines.Length == 0)
                        {
                            Debug.LogWarning("No lines found in MockServerLog");
                            return;
                        }

                        foreach (var line in lines)
                        {
                            jsonEvents.Add(JObject.Parse(line));
                        }
                    }));
                }
            }

            var rumEvents = new List<RumEventDecoder>();
            var views = new Dictionary<string, JObject>();
            foreach (var json in jsonEvents
                .OrderBy(value => RumEventDecoder.jsonGetProp<long>(value, "_dd.document_version")))
            {
                var materialized = json;
                var type = json.Value<string>("type");
                if (type == "view" || type == "view_update")
                {
                    var key = $"{RumEventDecoder.jsonGetProp<string>(json, "session.id")}/" +
                              RumEventDecoder.jsonGetProp<string>(json, "view.id");
                    if (type == "view_update")
                    {
                        // A server reset can remove the baseline for an earlier test's view.
                        if (!views.TryGetValue(key, out var previous))
                        {
                            continue;
                        }

                        materialized = ApplyViewUpdate(previous, json);
                    }

                    views[key] = materialized;
                }

                var rumEvent = RumEventDecoder.fromJson(materialized);
                if (rumEvent != null)
                {
                    rumEvents.Add(rumEvent);
                }
                else
                {
                    Debug.LogWarning("Failed to decode RUMEvent from MockServerLog");
                }
            }

            return rumEvents.OrderBy(value => value.Date).ToList();
        }

        private static JObject ApplyViewUpdate(JObject previous, JObject update)
        {
            var view = (JObject)previous.DeepClone();
            // Identity fields are always forwarded; their absence explicitly clears them.
            view.Remove("usr");
            view.Remove("account");
            foreach (var property in update.Properties())
            {
                if (property.Name == "view")
                {
                    foreach (var field in ((JObject)property.Value).Properties())
                    {
                        view["view"][field.Name] = field.Value.DeepClone();
                    }
                }
                else
                {
                    view[property.Name] = property.Value.DeepClone();
                }
            }

            view["type"] = "view";
            return view;
        }

        public static List<RumSessionDecoder> RumSessionsFromEvents(List<RumEventDecoder> events)
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
}
