// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Text;
using Datadog.Unity.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Sends EVP telemetry events (exposures and flag evaluations) to Datadog intake endpoints.
    /// </summary>
    internal class EvpTelemetrySender
    {
        private readonly string _clientToken;
        private readonly string _exposureEndpoint;
        private readonly string _evaluationEndpoint;
        private readonly IInternalLogger _logger;
        private readonly BatchContext _batchContext;

        public EvpTelemetrySender(
            string clientToken,
            string exposureEndpoint,
            string evaluationEndpoint,
            string env,
            IInternalLogger logger)
        {
            _clientToken = clientToken;
            _exposureEndpoint = exposureEndpoint;
            _evaluationEndpoint = evaluationEndpoint;
            _logger = logger;
            _batchContext = BuildBatchContext(env);
        }

        /// <summary>
        /// Sends a single exposure event to the exposure intake endpoint.
        /// Format: NDJSON (newline-delimited JSON), Content-Type: text/plain; charset=utf-8.
        /// </summary>
        public void SendExposure(ExposureEvent exposure)
        {
            if (exposure == null)
            {
                return;
            }

            var json = JsonConvert.SerializeObject(exposure) + "\n";
            SendRequest(_exposureEndpoint, "text/plain; charset=utf-8", json, "exposure event");
        }

        /// <summary>
        /// Sends a batch of flag evaluation events to the evaluation intake endpoint.
        /// Format: JSON with BatchedFlagEvaluations structure, Content-Type: application/json.
        /// </summary>
        public void SendEvaluations(List<FlagEvaluationEvent> evaluations)
        {
            if (evaluations == null || evaluations.Count == 0)
            {
                return;
            }

            var json = JsonConvert.SerializeObject(new BatchPayload
            {
                Context = _batchContext,
                FlagEvaluations = evaluations,
            });
            SendRequest(_evaluationEndpoint, "application/json", json, "evaluation events");
        }

        private void SendRequest(string endpoint, string contentType, string json, string eventDescription)
        {
            try
            {
                var bodyBytes = Encoding.UTF8.GetBytes(json);
                var url = AppendDdSource(endpoint);
                var request = new UnityWebRequest(url, "POST");
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", contentType);
                request.SetRequestHeader("dd-api-key", _clientToken);
                request.SetRequestHeader("dd-evp-origin", "unity");
                request.SetRequestHeader("dd-evp-origin-version", DatadogSdk.SdkVersion);

                var operation = request.SendWebRequest();
                operation.completed += _ =>
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        _logger?.Log(Logs.DdLogLevel.Warn, $"Failed to send {eventDescription}: {request.error}");
                    }
                    request.Dispose();
                };
            }
            catch (Exception e)
            {
                _logger?.TelemetryError($"Error sending {eventDescription}", e);
            }
        }

        private static BatchContext BuildBatchContext(string env)
        {
            return new BatchContext
            {
                Device = new DeviceInfo
                {
                    Name = SystemInfo.deviceName,
                    Type = GetDeviceType(),
                    Brand = "Unity",
                    Model = SystemInfo.deviceModel,
                },
                Os = new OsInfo
                {
                    Name = SystemInfo.operatingSystemFamily.ToString(),
                    Version = SystemInfo.operatingSystem,
                },
                Service = Application.identifier ?? Application.productName,
                Version = Application.version,
                Env = !string.IsNullOrEmpty(env) ? env : "prod",
            };
        }

        private static string GetDeviceType()
        {
            switch (SystemInfo.deviceType)
            {
                case DeviceType.Handheld: return "mobile";
                case DeviceType.Console: return "console";
                case DeviceType.Desktop: return "desktop";
                default: return "other";
            }
        }

        private static string AppendDdSource(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return url;
            }

            var builder = new UriBuilder(url);
            var query = builder.Query?.TrimStart('?') ?? string.Empty;
            if (!query.Contains("ddsource"))
            {
                builder.Query = query.Length > 0 ? query + "&ddsource=unity" : "ddsource=unity";
            }

            return builder.ToString();
        }

        private class BatchPayload
        {
            [JsonProperty("context")]
            public BatchContext Context { get; set; }

            [JsonProperty("flagEvaluations")]
            public List<FlagEvaluationEvent> FlagEvaluations { get; set; }
        }

        private class BatchContext
        {
            [JsonProperty("device")]
            public DeviceInfo Device { get; set; }

            [JsonProperty("os")]
            public OsInfo Os { get; set; }

            [JsonProperty("service")]
            public string Service { get; set; }

            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("env")]
            public string Env { get; set; }
        }

        private class DeviceInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("brand")]
            public string Brand { get; set; }

            [JsonProperty("model")]
            public string Model { get; set; }
        }

        private class OsInfo
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("version")]
            public string Version { get; set; }
        }
    }
}
