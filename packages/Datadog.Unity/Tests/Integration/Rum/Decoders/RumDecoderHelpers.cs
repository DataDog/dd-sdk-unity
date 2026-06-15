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
            var rumEvents = new List<RumEventDecoder>();
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

                        if (lines.Length == 1 && lines[0].StartsWith("["))
                        {
                            // Sent as an array instead of JSONL
                            var jsonArray = JArray.Parse(lines[0]);
                            foreach (var jsonRum in jsonArray)
                            {
                                var rumEvent = RumEventDecoder.fromJson(jsonRum as JObject);
                                if (rumEvent != null)
                                {
                                    rumEvents.Add(rumEvent);
                                }
                                else
                                {
                                    Debug.LogWarning("Failed to decode RUMEvent from MockServerLog");
                                }
                            }
                        }
                        else
                        {
                            // Sent as JSONL
                            foreach (var line in lines)
                            {
                                var jsonRum = JObject.Parse(line);
                                var rumEvent = RumEventDecoder.fromJson(jsonRum);
                                if (rumEvent != null)
                                {
                                    rumEvents.Add(rumEvent);
                                }
                                else
                                {
                                    Debug.LogWarning("Failed to decode RUMEvent from MockServerLog");
                                }
                            }
                        }
                    }));
                }
            }

            return rumEvents;
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
