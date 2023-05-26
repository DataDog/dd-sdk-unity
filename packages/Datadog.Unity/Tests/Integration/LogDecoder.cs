// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;

namespace Datadog.Unity.Tests.Integration
{
    public class LogDecoder
    {
        private Dictionary<string, object> _rawJson;

        public LogDecoder(Dictionary<string, object> rawJson)
        {
            _rawJson = rawJson;
        }

        public string Status
        {
            get { return _rawJson["status"] as string; }
        }

        public string Message
        {
            get { return _rawJson["message"] as string; }
        }

        public string ServiceName
        {
            get { return _rawJson["service"] as string; }
        }
    }
}
