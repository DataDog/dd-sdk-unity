// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

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

        public async Task Clear()
        {
            await _client.GetAsync($"{_endpoint}/reset");
        }

        public async Task<List<MockServerLog>> PollRequests(TimeSpan duration, Func<List<MockServerLog>, bool> parseRequests)
        {
            var timeoutTime = DateTime.Now + duration;

            var stopPolling = false;
            do
            {
                var inspect = await _client.GetAsync($"{_endpoint}/inspect_requests/");
                if (inspect.StatusCode == HttpStatusCode.OK)
                {
                    try
                    {
                        var content = await inspect.Content.ReadAsStringAsync();
                        var contractResolver = new DefaultContractResolver
                        {
                            NamingStrategy = new SnakeCaseNamingStrategy(),
                        };
                        var serverLog = JsonConvert.DeserializeObject<List<MockServerLog>>(content, new JsonSerializerSettings()
                        {
                            ContractResolver = contractResolver,
                        });

                        if (parseRequests(serverLog))
                        {
                            return serverLog;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"Caught an exception deserializing response: {e}.");
                    }
                }

                await Task.Delay(500);
            }
            while (!stopPolling && DateTime.Now < timeoutTime);

            return new List<MockServerLog>();
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
                    var parts = (header[..colonIndex], header[(colonIndex + 1) ..].Trim());
                    headerDict[parts.Item1] = parts.Item2;
                }

                return headerDict;
            }
        }

        public string DecompressedData { get; set; }

        public T ParseDecompressedJsonData<T>()
        {
            return JsonConvert.DeserializeObject<T>(DecompressedData);
        }
    }
}
