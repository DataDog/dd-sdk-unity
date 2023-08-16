// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumResourceEventDecoder : RumEventDecoder
    {

        public RumViewInfoDecoder ViewInfo { get; private set; }

        public string Url => jsonGetProp<string>(rumEvent, "resource.url");

        public int StatusCode => jsonGetProp<int>(rumEvent, "resource.status_code");

        public string ResourceType => jsonGetProp<string>(rumEvent, "resource.type");

        public int Duration => jsonGetProp<int>(rumEvent, "resource.duration");

        public string Method => jsonGetProp<string>(rumEvent, "resource.method");

        public int Size => jsonGetProp<int>(rumEvent, "resource.size");

        public RumResourceEventDecoder(JObject rawJson)
            : base(rawJson)
        {
            ViewInfo = new RumViewInfoDecoder(rawJson["view"] as JObject);
        }
    }
}
