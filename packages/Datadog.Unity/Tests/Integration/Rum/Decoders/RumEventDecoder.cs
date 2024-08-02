// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

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

        public JObject Attributes => rumEvent["context"] as JObject;

        public JObject FeatureFlags => rumEvent["feature_flags"] as JObject;

        public string UserName => jsonGetProp<string>(rumEvent, "usr.name");

        public string UserId => jsonGetProp<string>(rumEvent, "usr.id");

        public string UserEmail => jsonGetProp<string>(rumEvent, "usr.email");

        public JObject UserAttributes => rumEvent["usr"] as JObject;

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
                case "resource": return new RumResourceEventDecoder(eventJson);
                case "telemetry": return new RumTelemetryEventDecoder(eventJson);
                case "long_task": return new RumLongTaskEventDecoder(eventJson);
                default:
                    Debug.Log($"Unknown RUM event type: {type}");
                    break;
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
