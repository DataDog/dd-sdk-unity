// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity.Rum;
using Newtonsoft.Json;

namespace Datadog.Unity.WebGL
{
    public class DatadogWebGLRum : IDdRum
    {
        private class BrowerSdkConfig
        {
            public string applicationId { get; set; }
            public string clientToken { get; set; }
            public string site { get; set; }
            public float sessionSampleRate { get; set; }
            public float sessionReplaySampleRate { get; set; } = 0f;
            public string service { get; set; }
            public string env { get; set; }
            public string version { get; set; }
            public string proxy { get; set; }
            // TODO: allowedTracingUrls
            public float traceSampleRate { get; set; }
            public string traceContextInjection { get; set; }
            public bool trackFrustrations { get; set; }
            public bool trackViewsManually { get; set; } = true;
            public bool trackResources { get; set; }
            public bool trackLongTasks { get; set; }
            public List<string> enableExperimentalFeatures { get; set; } = new List<string>();
        }

        public void Init(DatadogConfigurationOptions options)
        {
            var browserSdkConfig = new BrowerSdkConfig
            {
                applicationId = options.RumApplicationId,
                clientToken = options.ClientToken,
                site = options.Site.ToWebValue(),
                sessionSampleRate = options.SessionSampleRate,
                service = options.ServiceName,
                env = options.Env,
                // TODO: Version
                version = "unknown",
                traceSampleRate = options.TraceSampleRate,
                trackFrustrations = true,
                trackResources = true,
                trackLongTasks = true,
                proxy = options.CustomEndpoint,
            };
            var configurationJson = JsonConvert.SerializeObject(browserSdkConfig);
            // TODO: Check the return value of this
            DDRum_InitRum(configurationJson);
        }

        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null)
        {
            var viewName = key;
            if (name != null)
            {
                viewName = name;
            }

            var attributesJson = "{}";
            if (attributes != null)
            {
                attributesJson = JsonConvert.SerializeObject(attributes);
            }

            DDRum_StartView(viewName, attributesJson);
        }

        public void StopView(string key, Dictionary<string, object> attributes = null)
        {
            // Browser SDK does not support stopping views
        }

        public void AddAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            if (name == null)
            {
                return;
            }

            var attributesJson = "{}";
            if (attributes != null)
            {
                attributesJson = JsonConvert.SerializeObject(attributes);
            }

            DDRum_AddAction(
                name,
                attributesJson);
        }

        public void StartAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            // Browser SDK does not support start / stop action
        }

        public void StopAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            // Browser SDK does not support start / stop action
        }

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
            if (error == null)
            {
                return;
            }

            var attributesJson = "{}";
            if (attributes != null)
            {
                attributesJson = JsonConvert.SerializeObject(attributes);
            }

            DDRum_AddError(
                error.GetType().Name,
                error.Message,
                error.StackTrace,
                attributesJson);
        }

        public void StartResource(string key, RumHttpMethod httpMethod, string url, Dictionary<string, object> attributes = null)
        {
            // Browser SDK does not support manual resource tracking
        }

        public void StopResource(string key, RumResourceType kind, int? statusCode = null, long? size = null,
            Dictionary<string, object> attributes = null)
        {
            // Browser SDK does not support manual resource tracking
        }

        public void StopResourceWithError(string key, string errorType, string errorMessage, Dictionary<string, object> attributes = null)
        {
            // Browser SDK does not support manual resource tracking
        }

        public void StopResource(string key, Exception error, Dictionary<string, object> attributes = null)
        {
            // Browser SDK does not support manual resource tracking
        }

        public void AddAttribute(string key, object value)
        {
            if (key == null)
            {
                // Not an error, but don't bother calling to platform
                return;
            }

            var jsonArg = new Dictionary<string, object>()
            {
                { "key", key },
                { "value", value },
            };
            var jsonString = JsonConvert.SerializeObject(jsonArg);
            DDRum_AddAttribute(jsonString);
        }

        public void RemoveAttribute(string key)
        {
            if (key == null)
            {
                // Not an error, but don't bother calling to platform
                return;
            }

            DDRum_RemoveAttribute(key);
        }

        public void AddFeatureFlagEvaluation(string key, object value)
        {
            if (key == null)
            {
                // Not an error, but don't bother calling to platform
                return;
            }

            var jsonFeatureFlag = new Dictionary<string, object>()
            {
                { "name", key },
                { "value", value },
            };
            var jsonString = JsonConvert.SerializeObject(jsonFeatureFlag);
            DDRum_AddFeatureFlagEvaluation(jsonString);
        }

        public void StopSession()
        {
            DDRum_StopSession();
        }

        [DllImport("__Internal")]
        private static extern bool DDRum_InitRum(string configurationJson);

        [DllImport("__Internal")]
        private static extern void DDRum_AddAttribute(string attributeJson);

        [DllImport("__Internal")]
        private static extern void DDRum_RemoveAttribute(string name);

        [DllImport("__Internal")]
        private static extern void DDRum_AddTiming(string name);

        [DllImport("__Internal")]
        private static extern void DDRum_AddAction(string name, string attributeJson);

        [DllImport("__Internal")]
        private static extern void DDRum_AddError(string errorKind, string errorMessage, string errorStackTrace, string attributeJson);

        [DllImport("__Internal")]
        private static extern void DDRum_AddFeatureFlagEvaluation(string attributeJson);

        [DllImport("__Internal")]
        private static extern void DDRum_StartView(string viewName, string attributesJson);

        [DllImport("__Internal")]
        private static extern void DDRum_StopSession();
    }
}
