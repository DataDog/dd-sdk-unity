// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumTelemetryEventDecoder : RumEventDecoder
    {
        public string TelemetryType => jsonGetProp<string>(rumEvent, "telemetry.type");
        
        public string Status => jsonGetProp<string>(rumEvent, "telemetry.status");

        public string Message => jsonGetProp<string>(rumEvent, "telemetry.message");

        public string ErrorStack => jsonGetProp<string>(rumEvent, "telemetry.error.stack");

        public string ErrorKind => jsonGetProp<string>(rumEvent, "telemetry.error.kind");

        public RumTelemetryEventDecoder(JObject rawJson)
            : base(rawJson)
        {
        }
    }
}
