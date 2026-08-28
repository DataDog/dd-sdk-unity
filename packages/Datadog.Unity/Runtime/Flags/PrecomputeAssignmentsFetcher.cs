// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Datadog.Unity.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Fetches precomputed flag assignments from the Datadog precompute endpoint.
    /// </summary>
    internal class PrecomputeAssignmentsFetcher
    {
        private readonly string _endpointUrl;
        private readonly string _clientToken;
        private readonly string _applicationId;
        private readonly string _env;
        private readonly IInternalLogger _logger;
        private readonly IAssignmentRequestTransport _transport;

        public PrecomputeAssignmentsFetcher(
            string endpointUrl,
            string clientToken,
            string applicationId,
            string env,
            IInternalLogger logger,
            int requestTimeoutSeconds = AssignmentRequestRetryPolicy.DisabledTimeoutSeconds,
            int requestRetryCount = AssignmentRequestRetryPolicy.DefaultRetryCount,
            Func<double> randomValue = null,
            Func<DateTimeOffset> utcNow = null,
            Func<int, CancellationToken, Task> delay = null,
            IAssignmentRequestTransport assignmentRequestTransport = null)
        {
            _endpointUrl = endpointUrl;
            _clientToken = clientToken;
            _applicationId = applicationId;
            _env = env;
            _logger = logger;
            if (assignmentRequestTransport != null)
            {
                // A caller-supplied transport is a complete assignment-only
                // override. Do not add the scalar convenience policy again.
                _transport = assignmentRequestTransport;
            }
            else
            {
                var timeout = AssignmentRequestRetryPolicy.NormalizeTimeoutSeconds(requestTimeoutSeconds);
                var retries = AssignmentRequestRetryPolicy.NormalizeRetryCount(requestRetryCount);
                var timedTransport = AssignmentRequestTransports.Default.WithTimeout(timeout);
                _transport = AssignmentRequestTransports.BuildRetryTransport(
                    timedTransport,
                    retries,
                    randomValue,
                    utcNow,
                    delay);
            }
        }

        /// <summary>
        /// Fetches precomputed assignments for the given evaluation context.
        /// Uses a callback since UnityWebRequest can be used from coroutines.
        /// </summary>
        public async void Fetch(FlagsEvaluationContext context, Action<Dictionary<string, FlagAssignment>> onComplete)
        {
            Dictionary<string, FlagAssignment> flags = null;
            try
            {
                var requestBody = BuildRequestBody(context);
                var response = await _transport.SendAsync(CreateRequest(requestBody), CancellationToken.None);
                if (response == null)
                    throw new InvalidOperationException("Assignment transport returned a null response.");

                if (response.Result == AssignmentRequestResult.Success &&
                    response.StatusCode >= 200 &&
                    response.StatusCode <= 299)
                {
                    flags = ParseResponse(response.Body);
                }
                else
                {
                    var errorDetail = ExtractServerError(response.Body, response.StatusCode);
                    _logger?.Log(Logs.DdLogLevel.Warn,
                        $"Failed to fetch flag assignments (HTTP {response.StatusCode}): {errorDetail}");
                }
            }
            catch (Exception e)
            {
                _logger?.Log(Logs.DdLogLevel.Warn, $"Error fetching flag assignments: {e.Message}");
                _logger?.TelemetryError("Error fetching flag assignments", e);
            }
            finally
            {
                onComplete?.Invoke(flags);
            }
        }

        internal AssignmentRequest CreateRequest(string body)
        {
            var headers = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/vnd.api+json",
                ["dd-client-token"] = _clientToken,
            };
            if (!string.IsNullOrEmpty(_applicationId))
                headers["dd-application-id"] = _applicationId;
            return new AssignmentRequest("POST", _endpointUrl, body, headers);
        }

        private string BuildRequestBody(FlagsEvaluationContext context)
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

        internal static Dictionary<string, FlagAssignment> ParseResponse(string json)
        {
            var flags = new Dictionary<string, FlagAssignment>();

            if (string.IsNullOrEmpty(json))
            {
                return flags;
            }

            AssignmentsResponseDto response;
            try
            {
                response = JsonConvert.DeserializeObject<AssignmentsResponseDto>(json);
            }
            catch
            {
                return flags;
            }

            var flagsDict = response?.Data?.Attributes?.Flags;
            if (flagsDict == null)
            {
                return flags;
            }

            foreach (var kvp in flagsDict)
            {
                var dto = kvp.Value;
                flags[kvp.Key] = new FlagAssignment(
                    variationType: dto.VariationType,
                    variationValue: dto.VariationValue,
                    doLog: dto.DoLog,
                    allocationKey: dto.AllocationKey,
                    variationKey: dto.VariationKey,
                    reason: dto.Reason);
            }

            return flags;
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

        private class AssignmentsResponseDto
        {
            [JsonProperty("data")]
            public AssignmentsResponseDataDto Data { get; set; }
        }

        private class AssignmentsResponseDataDto
        {
            [JsonProperty("attributes")]
            public AssignmentsResponseAttributesDto Attributes { get; set; }
        }

        private class AssignmentsResponseAttributesDto
        {
            [JsonProperty("flags")]
            public Dictionary<string, FlagAssignmentDto> Flags { get; set; }
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
