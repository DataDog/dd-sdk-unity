// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Logging
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

        public string ApplicationVersion
        {
            get
            {
#if UNITY_IOS
                return _rawJson["version"] as string;
#else
                var tag = Tags.FirstOrDefault(e => e.StartsWith("version:"));
                return tag == null ? string.Empty : tag.Split(":")[1];
#endif
            }
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

        public string UserId
        {
            get { return GetNestedProperty<string>("usr.id", true); }
        }

        public string UserName
        {
            get { return GetNestedProperty<string>("usr.name", true); }
        }

        public string UserEmail
        {
            get { return GetNestedProperty<string>("usr.email", true); }
        }

        public Dictionary<string, object> UserExtraInfo
        {
            get
            {
                if (_rawJson.TryGetValue("usr", out var value))
                {
                    return ((JObject)value).ToObject<Dictionary<string, object>>();
                }

                return _rawJson.Where(e => e.Key.StartsWith("usr."))
                    .ToDictionary(e => e.Key.Substring(4), e => e.Value);
            }
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
                        var json = schema.ParseDecompressedJsonData<Dictionary<string, object>>();
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

        // The `isNested` property overrides the default behavior for getting the
        // requested property. For Android and WebGL, dot notated properties
        // are nested as objects. On iOS and Standalone, dot notated are held as
        // a string. But, some properties on standalone builds (usr) are nested.
        private T GetNestedProperty<T>(string key, bool? isNested = null)
        {
#if UNITY_ANDROID || UNITY_WEBGL
            isNested ??= true
#elif UNITY_IOS
            // iOS is always unnessted
            isNested = false;
#else
            // False by default
            isNested ??= false;
#endif
            if (isNested.Value)
            {
                var parts = key.Split('.');
                var lookupMap = _rawJson;
                for (int i = 0; i < (parts.Length - 1); i++)
                {
                    lookupMap = ((JObject)lookupMap[parts[i]]).ToObject<Dictionary<string, object>>();
                }

                if (lookupMap.TryGetValue(parts.Last(), out var value))
                {
                    return (T)value;
                }
            }
            else
            {
                if (_rawJson.TryGetValue(key, out var value))
                {
                    return (T)value;
                }
            }

            return default(T);
        }
    }
}
