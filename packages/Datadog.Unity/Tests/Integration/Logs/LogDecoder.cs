// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Logs
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

        public string SdkVersion
        {
            get
            {
#if UNITY_WEBGL
                const string prefix = "sdk_version:";
                var tag = Tags.FirstOrDefault(value => value.StartsWith(prefix));
                return tag?.Substring(prefix.Length);
#else
                return GetNestedProperty<string>("logger.version");
#endif
            }
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
            get { return GetNestedProperty<string>("usr.id"); }
        }

        public string UserName
        {
            get { return GetNestedProperty<string>("usr.name"); }
        }

        public string UserEmail
        {
            get { return GetNestedProperty<string>("usr.email"); }
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

        // Reads nested objects and literal dot-separated keys through the same property path.
        private T GetNestedProperty<T>(string key)
        {
            if (_rawJson.TryGetValue(key, out var value))
            {
                return (T)value;
            }

            var parts = key.Split('.');
            if (!_rawJson.TryGetValue(parts[0], out var root) || !(root is JObject nested))
            {
                return default(T);
            }

            var token = nested.SelectToken(string.Join(".", parts.Skip(1)));
            return token == null ? default(T) : token.ToObject<T>();
        }
    }
}
