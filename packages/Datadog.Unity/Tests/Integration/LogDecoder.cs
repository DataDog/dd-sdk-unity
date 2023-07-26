// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration
{
    public class LogDecoder
    {
        private readonly Dictionary<string, object> _rawJson;

        public LogDecoder(Dictionary<string, object> rawJson)
        {
            _rawJson = rawJson;
        }

        public Dictionary<string, string> Headers { get; private set; }

        public Dictionary<string, object> RawJson
        {
            get { return _rawJson; }
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

        public string RawTags
        {
            get { return _rawJson["ddtags"] as string; }
        }

        public string[] Tags
        {
            get { return RawTags.Split(','); }
        }

        public string LoggerName
        {
            get { return GetNestedProperty<string>("logger.name"); }
        }

        public string ErrorKind
        {
            get { return GetNestedProperty<string>("error.kind"); }
        }

        public string ErrorMessage
        {
            get { return GetNestedProperty<string>("error.message"); }
        }

        public string ErrorStack
        {
            get { return GetNestedProperty<string>("error.stack"); }
        }

        public static List<LogDecoder> LogsFromMockServer(List<MockServerLog> mockServerLogs)
        {
            var logs = new List<LogDecoder>();
            foreach (var mockLog in mockServerLogs)
            {
                if (mockLog.Endpoint.Contains("/logs"))
                {
                    mockLog.Requests.ForEach((e) => e.Schemas.ForEach((schema) =>
                    {
                        var json = schema.ParseDecompressedJsonData<List<Dictionary<string, object>>>();
                        foreach (var jsonLog in json)
                        {
                            var log = new LogDecoder(jsonLog)
                            {
                                Headers = schema.ParsedHeaders,
                            };
                            logs.Add(log);
                        }
                    }));
                }
            }

            return logs;
        }

        private T GetNestedProperty<T>(string key)
        {
#if UNITY_ANDROID
            var parts = key.Split('.');
            var lookupMap = _rawJson;
            for (int i = 0; i < (parts.Length - 1); i++)
            {
                lookupMap = ((JObject)lookupMap[parts[i]]).ToObject<Dictionary<string, object>>();
            }

            return (T)lookupMap[parts.Last()];
#else
            return (T)_rawJson[key];
#endif
        }
    }
}
