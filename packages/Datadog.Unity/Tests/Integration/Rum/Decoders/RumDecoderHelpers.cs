// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

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
