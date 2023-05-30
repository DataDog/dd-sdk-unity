// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Datadog.Unity.Tests.Integration
{
    public class MockServerHelper
    {
        private readonly HttpClient _client = new();
        private string _endpoint;

        public MockServerHelper()
        {
            // Read the mock server address from Unity assets. Assume
            // the custom endpoint is the mock server
            var configuration = DatadogConfigurationOptions.Load();
            _endpoint = configuration.CustomEndpoint;
        }

        public async Task<List<Dictionary<string, object>>> PollRequests(TimeSpan duration, int count)
        {
            List<Dictionary<string, object>> requests = new();
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
                        var json = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(content);
                        if (json.Count >= count)
                        {
                            requests = json;
                            stopPolling = true;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Log($"Caught an exception deserializing response: {e}.");
                    }
                }

                await Task.Delay(500);
            } while (!stopPolling && DateTime.Now < timeoutTime);

            return requests;
        }
    }
}