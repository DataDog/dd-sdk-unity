// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
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
                        result.Add(ParseBatch(data));
                    }
                }
            }
            return result;
        }

        private static BatchedEvaluations ParseBatch(string data)
        {
            var dto = JsonConvert.DeserializeObject<BatchDto>(data);
            if (dto == null)
            {
                return new BatchedEvaluations { Context = null, FlagEvaluations = new List<EvaluationRecord>() };
            }

            BatchContext context = null;
            if (dto.Context != null)
            {
                context = new BatchContext
                {
                    Env = dto.Context.Env,
                    Service = dto.Context.Service,
                    Version = dto.Context.Version,
                    Device = dto.Context.Device,
                    Os = dto.Context.Os,
                };
            }

            var evals = new List<EvaluationRecord>();
            if (dto.FlagEvaluations != null)
            {
                foreach (var item in dto.FlagEvaluations)
                {
                    evals.Add(new EvaluationRecord
                    {
                        FlagKey = item.Flag?.Key,
                        VariantKey = item.Variant?.Key,
                        AllocationKey = item.Allocation?.Key,
                        TargetingKey = item.TargetingKey,
                        ErrorMessage = item.Error?.Message,
                        FirstEvaluation = item.FirstEvaluation,
                        LastEvaluation = item.LastEvaluation,
                        EvaluationCount = item.EvaluationCount,
                        RuntimeDefaultUsed = item.RuntimeDefaultUsed,
                    });
                }
            }

            return new BatchedEvaluations { Context = context, FlagEvaluations = evals };
        }

        private class BatchDto
        {
            [JsonProperty("context")]
            public ContextDto Context { get; set; }

            [JsonProperty("flagEvaluations")]
            public List<FlagEvaluationDto> FlagEvaluations { get; set; }
        }

        private class ContextDto
        {
            [JsonProperty("env")]
            public string Env { get; set; }

            [JsonProperty("service")]
            public string Service { get; set; }

            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("device")]
            public JObject Device { get; set; }

            [JsonProperty("os")]
            public JObject Os { get; set; }
        }

        private class FlagEvaluationDto
        {
            [JsonProperty("flag")]
            public KeyedDto Flag { get; set; }

            [JsonProperty("variant")]
            public KeyedDto Variant { get; set; }

            [JsonProperty("allocation")]
            public KeyedDto Allocation { get; set; }

            [JsonProperty("targeting_key")]
            public string TargetingKey { get; set; }

            [JsonProperty("error")]
            public ErrorDto Error { get; set; }

            [JsonProperty("first_evaluation")]
            public long FirstEvaluation { get; set; }

            [JsonProperty("last_evaluation")]
            public long LastEvaluation { get; set; }

            [JsonProperty("evaluation_count")]
            public int EvaluationCount { get; set; }

            [JsonProperty("runtime_default_used")]
            public bool? RuntimeDefaultUsed { get; set; }
        }

        private class KeyedDto
        {
            [JsonProperty("key")]
            public string Key { get; set; }
        }

        private class ErrorDto
        {
            [JsonProperty("message")]
            public string Message { get; set; }
        }

        public static List<EvaluationRecord> AllRecords(List<MockServerLog> logs)
        {
            return FromMockServer(logs).SelectMany(b => b.FlagEvaluations).ToList();
        }
    }
}
