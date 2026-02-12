// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Text;
using Datadog.Unity.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        public EvpTelemetrySender(
            string clientToken,
            string exposureEndpoint,
            string evaluationEndpoint,
            IInternalLogger logger)
        {
            _clientToken = clientToken;
            _exposureEndpoint = exposureEndpoint;
            _evaluationEndpoint = evaluationEndpoint;
            _logger = logger;
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

            try
            {
                var json = exposure.ToJson();
                var bodyBytes = Encoding.UTF8.GetBytes(json);

                var url = AppendDdSource(_exposureEndpoint);
                var request = new UnityWebRequest(url, "POST");
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "text/plain; charset=utf-8");
                request.SetRequestHeader("dd-api-key", _clientToken);
                request.SetRequestHeader("dd-evp-origin", "unity");
                request.SetRequestHeader("dd-evp-origin-version", DatadogSdk.SdkVersion);

                var operation = request.SendWebRequest();
                operation.completed += _ =>
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        _logger?.Log(Logs.DdLogLevel.Debug, $"Failed to send exposure event: {request.error}");
                    }
                    request.Dispose();
                };
            }
            catch (Exception e)
            {
                _logger?.TelemetryError("Error sending exposure event", e);
            }
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

            try
            {
                var json = BuildBatchedEvaluationsJson(evaluations);
                var bodyBytes = Encoding.UTF8.GetBytes(json);

                var url = AppendDdSource(_evaluationEndpoint);
                var request = new UnityWebRequest(url, "POST");
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("dd-api-key", _clientToken);
                request.SetRequestHeader("dd-evp-origin", "unity");
                request.SetRequestHeader("dd-evp-origin-version", DatadogSdk.SdkVersion);

                var operation = request.SendWebRequest();
                operation.completed += _ =>
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        _logger?.Log(Logs.DdLogLevel.Debug, $"Failed to send evaluation events: {request.error}");
                    }
                    request.Dispose();
                };
            }
            catch (Exception e)
            {
                _logger?.TelemetryError("Error sending evaluation events", e);
            }
        }

        private string BuildBatchedEvaluationsJson(List<FlagEvaluationEvent> evaluations)
        {
            var sb = new StringBuilder();
            sb.Append("{\"context\":");
            sb.Append(BuildBatchContextJson());
            sb.Append(",\"flagEvaluations\":[");

            for (var i = 0; i < evaluations.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(evaluations[i].ToJson());
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private string BuildBatchContextJson()
        {
            var context = new JObject
            {
                ["device"] = new JObject
                {
                    ["name"] = SystemInfo.deviceName,
                    ["type"] = GetDeviceType(),
                    ["brand"] = "Unity",
                    ["model"] = SystemInfo.deviceModel,
                },
                ["os"] = new JObject
                {
                    ["name"] = SystemInfo.operatingSystemFamily.ToString(),
                    ["version"] = SystemInfo.operatingSystem,
                },
                ["service"] = Application.identifier ?? Application.productName,
                ["version"] = Application.version,
                ["env"] = "prod",
            };
            return context.ToString(Formatting.None);
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
            var separator = url.Contains("?") ? "&" : "?";
            return url + separator + "ddsource=unity";
        }
    }
}
