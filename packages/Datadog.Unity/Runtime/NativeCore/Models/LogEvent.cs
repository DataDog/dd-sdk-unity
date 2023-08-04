// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Datadog.Unity.NativeCore.Models
{
    public class LogEvent
    {
        public DateTime Date { get; set; }

        public string Status { get; set; }

        public string Message { get; set; }

        public string ServiceName { get; set; }

        public ErrorInfo Error { get; set; }

        public LoggerInfo Logger { get; set; }

        public string Version { get; set; }

        public string Tags { get; set; }

        public class ErrorInfo
        {
            public string Kind { get; set; }

            public string Message { get; set; }

            public string Stack { get; set; }
        }

        public class LoggerInfo
        {
            public string Name { get; set; }

            public string Version { get; set; }

            public string ThreadName { get; set; }
        }

        public string Serialize()
        {
            var serializationSettings = new JsonSerializerSettings()
            {
                ContractResolver = new DefaultContractResolver()
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                    {
                        ProcessDictionaryKeys = false,
                    },
                },
            };
            return JsonConvert.SerializeObject(this, serializationSettings);
        }
    }
}
