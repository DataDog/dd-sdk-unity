// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumActionEventDecoder : RumEventDecoder
    {
        public RumViewInfoDecoder ViewInfo { get; private set; }

        public string ActionType => jsonGetProp<string>(rumEvent, "action.type");

        public string ActionName => jsonGetProp<string>(rumEvent, "action.target.name");

        public RumActionEventDecoder(JObject rawJson)
            : base(rawJson)
        {
            ViewInfo = new RumViewInfoDecoder(rawJson["view"] as JObject);
        }
    }
}
