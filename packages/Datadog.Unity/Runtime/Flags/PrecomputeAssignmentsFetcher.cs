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
    /// Fetches precomputed flag assignments from the Datadog precompute endpoint.
    /// </summary>
    internal class PrecomputeAssignmentsFetcher
    {
        public const int FetchTimeoutSeconds = 30;

        private readonly string _endpointUrl;
        private readonly string _clientToken;
        private readonly string _applicationId;
        private readonly string _env;
        private readonly IInternalLogger _logger;

        public PrecomputeAssignmentsFetcher(
            string endpointUrl,
            string clientToken,
            string applicationId,
            string env,
            IInternalLogger logger)
        {
            _endpointUrl = endpointUrl;
            _clientToken = clientToken;
            _applicationId = applicationId;
            _env = env;
            _logger = logger;
        }

        /// <summary>
        /// Fetches precomputed assignments for the given evaluation context.
        /// Uses a callback since UnityWebRequest can be used from coroutines.
        /// </summary>
        public void Fetch(FlagsEvaluationContext context, Action<Dictionary<string, FlagAssignment>> onComplete)
        {
            try
            {
                var requestBody = BuildRequestBody(context);
                var bodyBytes = Encoding.UTF8.GetBytes(requestBody);

                var request = new UnityWebRequest(_endpointUrl, "POST");
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = FetchTimeoutSeconds;
                request.SetRequestHeader("Content-Type", "application/vnd.api+json");
                request.SetRequestHeader("dd-client-token", _clientToken);

                if (!string.IsNullOrEmpty(_applicationId))
                {
                    request.SetRequestHeader("dd-application-id", _applicationId);
                }

                var operation = request.SendWebRequest();
                operation.completed += _ =>
                {
                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            _logger?.Log(Logs.DdLogLevel.Warn, $"Failed to fetch flag assignments: {request.error}");
                            onComplete?.Invoke(null);
                            return;
                        }

                        var responseText = request.downloadHandler.text;
                        var flags = ParseResponse(responseText);
                        onComplete?.Invoke(flags);
                    }
                    catch (Exception e)
                    {
                        _logger?.Log(Logs.DdLogLevel.Warn, $"Error parsing flag assignments: {e.Message}");
                        _logger?.TelemetryError("Error parsing flag assignments", e);
                        onComplete?.Invoke(null);
                    }
                    finally
                    {
                        request.Dispose();
                    }
                };
            }
            catch (Exception e)
            {
                _logger?.Log(Logs.DdLogLevel.Warn, $"Error fetching flag assignments: {e.Message}");
                _logger?.TelemetryError("Error fetching flag assignments", e);
                onComplete?.Invoke(null);
            }
        }

        private string BuildRequestBody(FlagsEvaluationContext context)
        {
            var subject = new JObject
            {
                ["targeting_key"] = context.TargetingKey,
            };

            if (context.Attributes.Count > 0)
            {
                subject["targeting_attributes"] = JObject.FromObject(context.Attributes);
            }

            var body = new JObject
            {
                ["data"] = new JObject
                {
                    ["type"] = "precompute-assignments-request",
                    ["attributes"] = new JObject
                    {
                        ["env"] = new JObject
                        {
                            ["name"] = _env,
                            ["dd_env"] = _env,
                        },
                        ["subject"] = subject,
                    },
                },
            };

            return body.ToString(Formatting.None);
        }

        internal static Dictionary<string, FlagAssignment> ParseResponse(string json)
        {
            var flags = new Dictionary<string, FlagAssignment>();

            if (string.IsNullOrEmpty(json))
            {
                return flags;
            }

            JObject parsed;
            try
            {
                parsed = JObject.Parse(json);
            }
            catch
            {
                return flags;
            }

            if (!(parsed["data"]?["attributes"]?["flags"] is JObject flagsObj))
            {
                return flags;
            }

            foreach (var prop in flagsObj.Properties())
            {
                if (!(prop.Value is JObject flagData))
                {
                    continue;
                }

                var variationType = flagData["variationType"]?.ToString();
                var variationValueToken = flagData["variationValue"];
                var doLog = flagData["doLog"]?.Value<bool>() ?? false;
                var allocationKey = flagData["allocationKey"]?.ToString();
                var variationKey = flagData["variationKey"]?.ToString();
                var reason = flagData["reason"]?.ToString();

                var variationValue = ParseVariationValue(variationType, variationValueToken);

                flags[prop.Name] = new FlagAssignment(
                    variationType: variationType,
                    variationValue: variationValue,
                    doLog: doLog,
                    allocationKey: allocationKey,
                    variationKey: variationKey,
                    reason: reason);
            }

            return flags;
        }

        private static object ConvertJToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        dict[prop.Name] = ConvertJToken(prop.Value);
                    }
                    return dict;
                case JTokenType.Array:
                    var list = new List<object>();
                    foreach (var item in (JArray)token)
                    {
                        list.Add(ConvertJToken(item));
                    }
                    return list;
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.Float:
                    return token.Value<double>();
                case JTokenType.String:
                    return token.Value<string>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                default:
                    return token.ToString();
            }
        }

        private static object ParseVariationValue(string variationType, JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            switch (variationType?.ToLowerInvariant())
            {
                case "boolean":
                    return token.Value<bool>();

                case "string":
                    return token.Value<string>();

                case "integer":
                    return token.Value<int>();

                case "number":
                case "float":
                    return token.Value<double>();

                case "object":
                    return ConvertJToken(token);

                default:
                    return token.ToString();
            }
        }
    }
}
