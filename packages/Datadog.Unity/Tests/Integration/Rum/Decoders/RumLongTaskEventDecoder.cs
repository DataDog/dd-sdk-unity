// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumLongTaskEventDecoder : RumEventDecoder
    {
        public RumViewInfoDecoder ViewInfo { get; private set; }

        public int Duration => jsonGetProp<int>(rumEvent, "long_task.duration");

        public RumLongTaskEventDecoder(JObject rawJson)
            : base(rawJson)
        {
            ViewInfo = new RumViewInfoDecoder(rawJson["view"] as JObject);
        }
    }
}
