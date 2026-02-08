// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Text;
using Datadog.Unity.Core;
using UnityEngine;
using UnityEngine.Networking;

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
                request.SetRequestHeader("Content-Type", "application/vnd.api+json");
                request.SetRequestHeader("Accept-Encoding", "gzip, deflate, br");
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
                            _logger.Log(Logs.DdLogLevel.Warn, $"Failed to fetch flag assignments: {request.error}");
                            onComplete?.Invoke(null);
                            return;
                        }

                        var responseText = request.downloadHandler.text;
                        var flags = ParseResponse(responseText);
                        onComplete?.Invoke(flags);
                    }
                    catch (Exception e)
                    {
                        _logger.Log(Logs.DdLogLevel.Warn, $"Error parsing flag assignments: {e.Message}");
                        _logger.TelemetryError("Error parsing flag assignments", e);
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
                _logger.Log(Logs.DdLogLevel.Warn, $"Error fetching flag assignments: {e.Message}");
                _logger.TelemetryError("Error fetching flag assignments", e);
                onComplete?.Invoke(null);
            }
        }

        private string BuildRequestBody(FlagsEvaluationContext context)
        {
            var sb = new StringBuilder();
            sb.Append("{\"data\":{\"type\":\"precompute-assignments-request\",\"attributes\":{");
            sb.AppendFormat("\"env\":{{\"name\":{0},\"dd_env\":{0}}}", JsonHelper.Escape(_env));
            sb.Append(",\"subject\":{");
            sb.AppendFormat("\"targeting_key\":{0}", JsonHelper.Escape(context.TargetingKey));

            if (context.Attributes.Count > 0)
            {
                sb.Append(",\"targeting_attributes\":");
                sb.Append(JsonHelper.DictionaryToJson(context.Attributes));
            }

            sb.Append("}}}}");
            return sb.ToString();
        }

        internal static Dictionary<string, FlagAssignment> ParseResponse(string json)
        {
            var flags = new Dictionary<string, FlagAssignment>();

            // Parse the JSON response: {"data":{"attributes":{"flags":{...}}}}
            // Using Unity's JsonUtility is limited for dynamic keys, so we use a minimal parser
            var parsed = MiniJson.Deserialize(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                return flags;
            }

            if (!parsed.TryGetValue("data", out var dataObj) || !(dataObj is Dictionary<string, object> data))
            {
                return flags;
            }

            if (!data.TryGetValue("attributes", out var attrObj) || !(attrObj is Dictionary<string, object> attributes))
            {
                return flags;
            }

            if (!attributes.TryGetValue("flags", out var flagsObj) || !(flagsObj is Dictionary<string, object> flagsDict))
            {
                return flags;
            }

            foreach (var kvp in flagsDict)
            {
                if (!(kvp.Value is Dictionary<string, object> flagData))
                {
                    continue;
                }

                var variationType = GetStringField(flagData, "variationType");
                var variationValueRaw = flagData.ContainsKey("variationValue") ? flagData["variationValue"] : null;
                var doLog = GetBoolField(flagData, "doLog");
                var allocationKey = GetStringField(flagData, "allocationKey");
                var variationKey = GetStringField(flagData, "variationKey");
                var reason = GetStringField(flagData, "reason");

                var variationValue = ParseVariationValue(variationType, variationValueRaw);

                flags[kvp.Key] = new FlagAssignment(
                    variationType: variationType,
                    variationValue: variationValue,
                    doLog: doLog,
                    allocationKey: allocationKey,
                    variationKey: variationKey,
                    reason: reason);
            }

            return flags;
        }

        private static object ParseVariationValue(string variationType, object rawValue)
        {
            if (rawValue == null)
            {
                return null;
            }

            switch (variationType?.ToLowerInvariant())
            {
                case "boolean":
                    if (rawValue is bool boolVal)
                    {
                        return boolVal;
                    }
                    if (rawValue is string boolStr)
                    {
                        return string.Equals(boolStr, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    return false;

                case "string":
                    return rawValue.ToString();

                case "integer":
                    if (rawValue is long longVal)
                    {
                        return (int)longVal;
                    }
                    if (rawValue is double dblAsInt)
                    {
                        return (int)dblAsInt;
                    }
                    if (rawValue is string intStr && int.TryParse(intStr, out var parsed))
                    {
                        return parsed;
                    }
                    return 0;

                case "number":
                case "float":
                    if (rawValue is double dblVal)
                    {
                        return dblVal;
                    }
                    if (rawValue is long longAsDbl)
                    {
                        return (double)longAsDbl;
                    }
                    if (rawValue is string dblStr && double.TryParse(dblStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedDbl))
                    {
                        return parsedDbl;
                    }
                    return 0.0;

                case "object":
                    return rawValue; // Return as-is (Dictionary<string, object> from MiniJson)

                default:
                    return rawValue;
            }
        }

        private static string GetStringField(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value) && value != null)
            {
                return value.ToString();
            }
            return null;
        }

        private static bool GetBoolField(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is bool b)
                {
                    return b;
                }
                if (value is string s)
                {
                    return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }
    }
}
