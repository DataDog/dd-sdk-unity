// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Flags
{
    public class ExposureEventDecoder
    {
        private readonly JObject _raw;

        public ExposureEventDecoder(JObject raw)
        {
            _raw = raw;
        }

        public string FlagKey => _raw["flag"]?["key"]?.Value<string>();
        public string AllocationKey => _raw["allocation"]?["key"]?.Value<string>();
        public string VariantKey => _raw["variant"]?["key"]?.Value<string>();
        public string SubjectId => _raw["subject"]?["id"]?.Value<string>();
        public Dictionary<string, object> SubjectAttributes
        {
            get
            {
                var attrs = _raw["subject"]?["attributes"];
                return attrs?.ToObject<Dictionary<string, object>>() ?? new Dictionary<string, object>();
            }
        }

        public static List<ExposureEventDecoder> FromMockServer(List<MockServerLog> logs)
        {
            var result = new List<ExposureEventDecoder>();
            foreach (var log in logs)
            {
                if (!log.Endpoint.Contains("/api/v2/exposures"))
                {
                    continue;
                }
                foreach (var req in log.Requests)
                {
                    foreach (var schema in req.Schemas)
                    {
                        var data = schema.DecompressedData ?? schema.Data ?? string.Empty;
                        foreach (var line in data.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
                        {
                            var obj = JObject.Parse(line);
                            result.Add(new ExposureEventDecoder(obj));
                        }
                    }
                }
            }
            return result;
        }
    }
}
