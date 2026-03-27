// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Flags.OpenFeature.Tests.Integration.Decoders
{
    public class BatchContext
    {
        public string Env { get; set; }
        public string Service { get; set; }
        public string Version { get; set; }
        public JObject Device { get; set; }
        public JObject Os { get; set; }
    }

    public class EvaluationRecord
    {
        public string FlagKey { get; set; }
        public string VariantKey { get; set; }
        public string AllocationKey { get; set; }
        public string TargetingKey { get; set; }
        public string ErrorMessage { get; set; }
        public long FirstEvaluation { get; set; }
        public long LastEvaluation { get; set; }
        public int EvaluationCount { get; set; }
        public bool? RuntimeDefaultUsed { get; set; }
    }

    public class BatchedEvaluations
    {
        public BatchContext Context { get; set; }
        public List<EvaluationRecord> FlagEvaluations { get; set; }
    }

    public class EvaluationEventDecoder
    {
        public static List<BatchedEvaluations> FromMockServer(List<MockServerLog> logs)
        {
            var result = new List<BatchedEvaluations>();
            foreach (var log in logs)
            {
                if (!log.Endpoint.Contains("/api/v2/flagevaluation"))
                {
                    continue;
                }
                foreach (var req in log.Requests)
                {
                    foreach (var schema in req.Schemas)
                    {
                        var data = schema.DecompressedData ?? schema.Data ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(data))
                        {
                            continue;
                        }
                        result.Add(ParseBatch(JObject.Parse(data)));
                    }
                }
            }
            return result;
        }

        private static BatchedEvaluations ParseBatch(JObject root)
        {
            var ctx = root["context"] as JObject;
            var context = ctx == null ? null : new BatchContext
            {
                Env = ctx["env"]?.Value<string>(),
                Service = ctx["service"]?.Value<string>(),
                Version = ctx["version"]?.Value<string>(),
                Device = ctx["device"] as JObject,
                Os = ctx["os"] as JObject,
            };

            var evals = new List<EvaluationRecord>();
            if (root["flagEvaluations"] is JArray arr)
            {
                foreach (var item in arr)
                {
                    var obj = (JObject)item;
                    evals.Add(new EvaluationRecord
                    {
                        FlagKey = obj["flag"]?["key"]?.Value<string>(),
                        VariantKey = obj["variant"]?["key"]?.Value<string>(),
                        AllocationKey = obj["allocation"]?["key"]?.Value<string>(),
                        TargetingKey = obj["targeting_key"]?.Value<string>(),
                        ErrorMessage = obj["error"]?["message"]?.Value<string>(),
                        FirstEvaluation = obj["first_evaluation"]?.Value<long>() ?? 0,
                        LastEvaluation = obj["last_evaluation"]?.Value<long>() ?? 0,
                        EvaluationCount = obj["evaluation_count"]?.Value<int>() ?? 0,
                        RuntimeDefaultUsed = obj["runtime_default_used"]?.Value<bool?>(),
                    });
                }
            }

            return new BatchedEvaluations { Context = context, FlagEvaluations = evals };
        }

        public static List<EvaluationRecord> AllRecords(List<MockServerLog> logs)
        {
            return FromMockServer(logs).SelectMany(b => b.FlagEvaluations).ToList();
        }
    }
}
