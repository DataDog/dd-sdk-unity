// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.Networking;

namespace Datadog.Unity.Tests.Integration
{
    public class MockServerHelper
    {
        private readonly HttpClient _client = new();
        private readonly string _endpoint;

        public MockServerHelper()
        {
            // Read the mock server address from Unity assets. Assume
            // the custom endpoint is the mock server
            var configuration = DatadogConfigurationOptions.Load();
            _endpoint = configuration.CustomEndpoint;
        }

        public IEnumerator Clear()
        {
            var request = UnityWebRequest.Get($"{_endpoint}/reset");
            yield return request.SendWebRequest();
        }

        public IEnumerator PollRequests(TimeSpan duration, Func<List<MockServerLog>, bool> parseRequests)
        {
            var timeoutTime = DateTime.Now + duration;
            var stopPolling = false;

            do
            {
                var request = UnityWebRequest.Get($"{_endpoint}/inspect_requests/");
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success && request.responseCode == (long)HttpStatusCode.OK)
                {
                    try
                    {
                        var content = request.downloadHandler.text;
                        var contractResolver = new DefaultContractResolver
                        {
                            NamingStrategy = new SnakeCaseNamingStrategy(),
                        };
                        var serverLog = JsonConvert.DeserializeObject<List<MockServerLog>>(content, new JsonSerializerSettings()
                        {
                            ContractResolver = contractResolver,
                        });
                        stopPolling = parseRequests(serverLog);
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"Caught an exception deserializing response: {e}\n{e.StackTrace}");
                    }
                }

                if (!stopPolling)
                {
                    yield return new WaitForSeconds(2.0f);
                }
            }
            while (!stopPolling && DateTime.Now < timeoutTime);
        }
    }

    public class MockServerLog
    {
        public string Endpoint { get; set; }

        public List<MockServerRequest> Requests { get; set; }
    }

    public class MockServerRequest
    {
        public string Method { get; set; }

        public string QueryString { get; set; }

        public NameValueCollection QueryParameters
        {
            get => HttpUtility.ParseQueryString(QueryString);
        }

        public Dictionary<string, string> Tags
        {
            get
            {
                var tagDict = new Dictionary<string, string>();
                var tagsValue = QueryParameters["tags"] ?? string.Empty;
                foreach (var tag in QueryString.Split(","))
                {
                    var colonIndex = tag.IndexOf(":", StringComparison.Ordinal);
                    if (colonIndex == -1)
                    {
                        tagDict[tag] = string.Empty;
                    }
                    else
                    {
                        var parts = (tag[..colonIndex], tag[(colonIndex + 1)..]);
                        tagDict[parts.Item1] = parts.Item2;
                    }
                }

                return tagDict;
            }
        }

        public List<MockServerSchema> Schemas { get; set; }
    }

    public class MockServerSchema
    {
        public List<string> Headers { get; set; }

        public Dictionary<string, string> ParsedHeaders
        {
            get
            {
                var headerDict = new Dictionary<string, string>();
                foreach (var header in Headers)
                {
                    var colonIndex = header.IndexOf(':');
                    var parts = (header[..colonIndex], header[(colonIndex + 1)..].Trim());
                    headerDict[parts.Item1.ToLower()] = parts.Item2;
                }

                return headerDict;
            }
        }

        [CanBeNull] public string DecompressedData { get; set; }

        [CanBeNull] public string Data { get; set; }

        public List<T> ParseDecompressedJsonData<T>()
        {
            var dataString = DecompressedData ?? Data;
            try
            {
                return JsonConvert.DeserializeObject<List<T>>(dataString);
            }
            catch (JsonException e)
            {
                // Try to split the data first by newlines
                return dataString.Split("\n")
                    .Select(line => JsonConvert.DeserializeObject<T>(line))
                    .ToList();
            }
        }
    }
}
