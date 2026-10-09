// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Text;
using Datadog.Unity.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Fetches precomputed flag assignments from the Datadog precompute endpoint.
    /// </summary>
    internal class PrecomputeAssignmentsFetcher
    {
        public const int FetchTimeoutSeconds = 30;
        private static readonly string CapabilitiesHeader = string.Join(",",
            new SortedSet<string>(StringComparer.Ordinal) { FlagKeyObfuscation.Capability });

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
        public virtual void Fetch(FlagsEvaluationContext context, Action<FlagAssignments> onComplete)
        {
            try
            {
                var request = BuildRequest(context);

                var operation = request.SendWebRequest();
                operation.completed += _ =>
                {
                    try
                    {
                        if (request.result != UnityWebRequest.Result.Success)
                        {
                            var errorDetail = ExtractServerError(request.downloadHandler?.text, request.responseCode);
                            _logger?.Log(Logs.DdLogLevel.Warn,
                                $"Failed to fetch flag assignments (HTTP {request.responseCode}): {errorDetail}");
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

        internal UnityWebRequest BuildRequest(FlagsEvaluationContext context)
        {
            var request = new UnityWebRequest(_endpointUrl, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(BuildRequestBody(context)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = FetchTimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/vnd.api+json");
            request.SetRequestHeader("dd-client-token", _clientToken);
            request.SetRequestHeader("X-DD-FEATURE-FLAGS-CAPABILITIES", CapabilitiesHeader);
            if (!string.IsNullOrEmpty(_applicationId))
            {
                request.SetRequestHeader("dd-application-id", _applicationId);
            }
            return request;
        }

        internal string BuildRequestBody(FlagsEvaluationContext context)
        {
            var dto = new AssignmentsRequestDto
            {
                Data = new AssignmentsRequestDataDto
                {
                    Attributes = new AssignmentsRequestAttributesDto
                    {
                        Env = new AssignmentsEnvDto { Name = _env, DdEnv = _env },
                        Subject = new AssignmentsSubjectDto
                        {
                            TargetingKey = context.TargetingKey,
                            TargetingAttributes = context.Attributes.Count > 0
                                ? context.Attributes
                                : null,
                        },
                    },
                },
            };
            return JsonConvert.SerializeObject(dto);
        }

        /// <summary>
        /// Extracts a human-readable error message from a server response body.
        /// Handles the two response shapes returned by the edge-assignments server:
        ///   - JSON:API:  {"errors":[{"title":"...","detail":"..."}]}
        ///   - Flat:      {"error":"..."} (Fastly-level panics / 405 handler)
        /// Falls through to a generic message if the body is absent or unparseable.
        /// </summary>
        internal static string ExtractServerError(string body, long httpCode)
        {
            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    var obj = JObject.Parse(body);

                    // JSON:API format: {"errors":[{"title":"...","detail":"..."}]}
                    var errors = obj["errors"] as JArray;
                    if (errors != null && errors.Count > 0)
                    {
                        var first = errors[0];
                        var title = first["title"]?.Value<string>();
                        var detail = first["detail"]?.Value<string>();

                        if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(detail))
                            return $"{title}: {detail}";
                        if (!string.IsNullOrEmpty(title))
                            return title;
                        if (!string.IsNullOrEmpty(detail))
                            return detail;
                    }

                    // Flat format: {"error":"..."} (Fastly catch-all / 405 handler)
                    var error = obj["error"]?.Value<string>();
                    if (!string.IsNullOrEmpty(error))
                        return error;
                }
                catch
                {
                    // Body is not valid JSON — fall through.
                }
            }

            return $"unreadable error response (HTTP {httpCode})";
        }

        internal static FlagAssignments ParseResponse(string json)
        {
            var flags = new Dictionary<string, FlagAssignment>();

            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            JObject response;
            try
            {
                response = JObject.Parse(json);
            }
            catch
            {
                return null;
            }

            var attributes = response["data"]?["attributes"] as JObject;
            var encoding = FlagKeyObfuscation.Read(attributes?["obfuscated"], attributes?["obfuscation"]);
            var flagsDict = attributes?["flags"]?.ToObject<Dictionary<string, FlagAssignmentDto>>();
            if (flagsDict == null)
            {
                return null;
            }

            foreach (var kvp in flagsDict)
            {
                if (encoding != null && !FlagKeyObfuscation.IsLowercaseHex(kvp.Key, 64))
                    throw new JsonSerializationException("Invalid obfuscated flag-map key.");
                var dto = kvp.Value;
                flags[kvp.Key] = new FlagAssignment(
                    variationType: dto.VariationType,
                    variationValue: dto.VariationValue,
                    doLog: dto.DoLog,
                    allocationKey: dto.AllocationKey,
                    variationKey: dto.VariationKey,
                    reason: dto.Reason);
            }

            return new FlagAssignments(flags, encoding);
        }

        private class AssignmentsRequestDto
        {
            [JsonProperty("data")]
            public AssignmentsRequestDataDto Data { get; set; }
        }

        private class AssignmentsRequestDataDto
        {
            [JsonProperty("type")]
            public string Type { get; set; } = "precompute-assignments-request";

            [JsonProperty("attributes")]
            public AssignmentsRequestAttributesDto Attributes { get; set; }
        }

        private class AssignmentsRequestAttributesDto
        {
            [JsonProperty("env")]
            public AssignmentsEnvDto Env { get; set; }

            [JsonProperty("subject")]
            public AssignmentsSubjectDto Subject { get; set; }
        }

        private class AssignmentsEnvDto
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("dd_env")]
            public string DdEnv { get; set; }
        }

        private class AssignmentsSubjectDto
        {
            [JsonProperty("targeting_key")]
            public string TargetingKey { get; set; }

            [JsonProperty("targeting_attributes", NullValueHandling = NullValueHandling.Ignore)]
            public IReadOnlyDictionary<string, string> TargetingAttributes { get; set; }
        }

        private class FlagAssignmentDto
        {
            [JsonProperty("variationType")]
            public string VariationType { get; set; }

            [JsonProperty("variationValue")]
            public JToken VariationValue { get; set; }

            [JsonProperty("doLog")]
            public bool DoLog { get; set; }

            [JsonProperty("allocationKey")]
            public string AllocationKey { get; set; }

            [JsonProperty("variationKey")]
            public string VariationKey { get; set; }

            [JsonProperty("reason")]
            public string Reason { get; set; }

        }
    }
}
