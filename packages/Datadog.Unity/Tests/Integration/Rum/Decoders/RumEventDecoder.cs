// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumEventDecoder
    {
        public readonly JObject rumEvent;

        public string EventType
        {
            get => rumEvent.Value<string>("type");
        }

        public string Session
        {
            get => jsonGetProp<string>(rumEvent, "session.id");
        }

        public long Date
        {
            get => jsonGetProp<long>(rumEvent, "date");
        }

        protected RumEventDecoder(JObject rawJson)
        {
            rumEvent = rawJson;
        }

        public static RumEventDecoder fromJson(JObject eventJson)
        {
            var type = eventJson.Value<string>("type");
            switch (type)
            {
                case "view": return new RumViewEventDecoder(eventJson);
                case "action": return new RumActionEventDecoder(eventJson);
                case "error": return new RumErrorEventDecoder(eventJson);
            }

            return null;
        }

        // Use dot notation to access a nested property in the rumData
        public static T jsonGetProp<T>(JObject eventJson, string key)
        {
            var keyParts = key.Split('.');
            var lastValue = eventJson[keyParts.First()] as JToken;
            if (lastValue == null)
            {
                return default;
            }

            for (int i = 1; i < keyParts.Length; ++i)
            {
                var keyPart = keyParts[i];
                lastValue = lastValue[keyPart] as JToken;
                if (lastValue == null)
                {
                    return default;
                }
            }

            return lastValue.Value<T>();
        }
    }
}
