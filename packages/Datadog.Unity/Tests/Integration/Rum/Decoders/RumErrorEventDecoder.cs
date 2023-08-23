// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumErrorEventDecoder : RumEventDecoder
    {
        public RumViewInfoDecoder ViewInfo { get; private set; }

        public string ErrorType => jsonGetProp<string>(rumEvent, "error.type");

        public string Message => jsonGetProp<string>(rumEvent, "error.message");

        public string Stack => jsonGetProp<string>(rumEvent, "error.stack");

        public string Source => jsonGetProp<string>(rumEvent, "error.source");

        public string SourceType => jsonGetProp<string>(rumEvent, "error.source_type");

        public string ResourceUrl => jsonGetProp<string>(rumEvent, "error.resource.url");

        public string ResourceMethod => jsonGetProp<string>(rumEvent, "error.resource.method");

        public RumErrorEventDecoder(JObject rawJson)
            : base(rawJson)
        {
            ViewInfo = new RumViewInfoDecoder(rawJson["view"] as JObject);
        }
    }
}
