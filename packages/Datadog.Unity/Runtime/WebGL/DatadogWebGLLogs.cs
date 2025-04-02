// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Runtime.InteropServices;
using Datadog.Unity.Logs;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Datadog.Unity.WebGL
{
    public class DatadogWebGLLogs
    {
        public void Init(DatadogConfigurationOptions options)
        {
            var logConfig = new InitOptions()
            {
                clientToken = options.ClientToken,
                env = options.Env,
                proxy = string.IsNullOrEmpty(options.CustomEndpoint) ? null : options.CustomEndpoint,
                site = SiteStringForSite(options.Site),
                service = options.ServiceName,
                // TODO: Version
            };

            var jsConfig = JsonConvert.SerializeObject(logConfig, new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            DDInitLogs(jsConfig);
        }

        public DatadogWebGLLogger CreateLogger(DatadogLoggingOptions options)
        {
            var loggerId = Guid.NewGuid().ToString();
            var logger = new DatadogWebGLLogger(options.RemoteLogThreshold, options.RemoteSampleRate, loggerId);
            DDCreateLogger(loggerId, "{}");
            return logger;
        }

        private string SiteStringForSite(DatadogSite site)
        {
            return site switch
            {
                DatadogSite.Us1 => "datadoghq.com",
                DatadogSite.Us3 => "us3.datadoghq.com",
                DatadogSite.Us5 => "us5.datadoghq.com",
                DatadogSite.Eu1 => "datadoghq.eu",
                DatadogSite.Us1Fed => "ddog-gov.com",
                DatadogSite.Ap1 => "ap1.datadoghq.com",
                _ => "datadoghq.com"
            };
        }

        private class InitOptions
        {
            public string clientToken;
            public string env;
            public string proxy;
            public string site;
            public string service;
            public string version;
        }

        [DllImport("__Internal")]
        private static extern void DDInitLogs(string jsonConfiguration);

        [DllImport("__Internal")]
        private static extern void DDCreateLogger(string loggerId, string configuration);
    }
}
