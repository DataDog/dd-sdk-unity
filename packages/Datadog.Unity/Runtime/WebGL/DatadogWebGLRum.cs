// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Datadog.Unity.Rum;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace Datadog.Unity.WebGL
{
    public class DatadogWebGLRum : IDdRumInternal
    {
        private readonly Dictionary<string, ResourceInfo> _resourceInfos = new ();

        public void Init(DatadogConfigurationOptions options)
        {
            var allowedTracingUrls = options.FirstPartyHosts.Select((e) =>
            {
                // Browser SDK matchers receive the full URL; native wrappers match the parsed host and port.
                // Exclude dots from each subdomain label to prevent overlapping repetitions and excessive backtracking.
                var hostRegex = $@"^https?:\/\/(?:[^./?#:]+\.)*{Regex.Escape(e.Host)}\/";
                return new AllowedTracingUrl()
                {
                    match = hostRegex,
                    propagatorTypes = e.TracingHeaderType.ToWebValue(),
                };
            }).ToList();

            var browserSdkConfig = new BrowerSdkConfig
            {
                applicationId = options.RumApplicationId,
                clientToken = options.ClientToken,
                site = options.Site.ToWebValue(),
                sessionSampleRate = options.SessionSampleRate,
                service = options.ServiceName,
                env = options.Env,
                version = Application.version,
                traceSampleRate = options.TraceSampleRate,
                trackUserInteractions = false,
                trackFrustrations = true,
                trackResources = true,
                trackLongTasks = true,
                proxy = string.IsNullOrEmpty(options.CustomEndpoint) ? null : options.CustomEndpoint,
                allowedTracingUrls = allowedTracingUrls,
                traceContextInjection = options.TraceContextInjection.ToWebValue(),
                source = "unity",
                trackResourceHeaders = new ResourceHeaderFilter(options.TrackResourceHeaders, options.CustomResourceHeaders, options.IncludeDefaultHeaders).ToBrowserSdkOption(),
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
                type.ToWebValue(),
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

        public void AddError(ErrorInfo error, RumErrorSource source, Dictionary<string, object> attributes = null)
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
                error.Type,
                error.Message,
                error.StackTrace,
                source.ToWebValue(),
                attributesJson);
        }

        public void StartResource(string key, RumHttpMethod httpMethod, string url, Dictionary<string, object> attributes)
        {
            _resourceInfos[key] = new ResourceInfo(httpMethod, url, attributes);

            var attributesJson = attributes != null ? JsonConvert.SerializeObject(attributes) : "{}";

            DDRum_StartResource(key, httpMethod.ToWebValue(), url, attributesJson);
        }

        public void StopResource(
            string key,
            RumResourceType kind,
            int? statusCode = null,
            long? size = null,
            Dictionary<string, object> attributes = null)
        {
            _resourceInfos.Remove(key, out ResourceInfo resourceInfo);
            if (resourceInfo == null)
            {
                return;
            }

            // Serialize options so omitted nullable numbers remain undefined in JavaScript.
            var stopOptions = new Dictionary<string, object>()
            {
                { "resourceKey", key },
                { "type", kind.ToWebValue() },
            };

            if (attributes != null)
            {
                stopOptions["context"] = attributes;
            }

            if (statusCode.HasValue)
            {
                stopOptions["statusCode"] = statusCode.Value;
            }

            if (size.HasValue)
            {
                stopOptions["size"] = size.Value;
            }

            DDRum_StopResource(resourceInfo.Url, JsonConvert.SerializeObject(stopOptions));
        }

        public void StopResourceWithError(string key, string errorType, string errorMessage, Dictionary<string, object> attributes = null)
        {
            StopResourceWithError(key, new ErrorInfo(errorType, errorMessage), attributes);
        }

        public void StopResourceWithError(string key, ErrorInfo error, Dictionary<string, object> attributes = null)
        {
            if (!_resourceInfos.TryGetValue(key, out ResourceInfo resourceInfo))
            {
                return;
            }

            StopResource(key, RumResourceType.Other, attributes: attributes);

            var errorAttributes = resourceInfo.Attributes;
            if (attributes != null)
            {
                if (errorAttributes == null)
                {
                    errorAttributes = new Dictionary<string, object>();
                }

                foreach (var attribute in attributes)
                {
                    errorAttributes[attribute.Key] = attribute.Value;
                }
            }

            var attributesJson = errorAttributes != null ? JsonConvert.SerializeObject(errorAttributes) : "{}";

            DDRum_AddResourceError(resourceInfo.Method.ToWebValue(), resourceInfo.Url, error.Type, error.Message, error.StackTrace, attributesJson);
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

        public void UpdateExternalRefreshRate(double frameTimeSeconds)
        {
            // Browser SDK does not support frame rate tracking
        }

        private class ResourceInfo
        {
            public ResourceInfo(RumHttpMethod httpMethod, string url, Dictionary<string, object> attributes)
            {
                Method = httpMethod;
                Url = url;
                Attributes = attributes;
            }

            public RumHttpMethod Method { get; private set; }

            public string Url { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        // Disable warning about fields being lower case to match the JSON we need to produce for web
#pragma warning disable SA1307 // Public fields must begin with upper-case letter
#pragma warning disable SA1310 // Field names must not contain underscore
        [Preserve]
        private class AllowedTracingUrl
        {
            [Preserve]
            public string match { get; set; }
            [Preserve]
            public List<string> propagatorTypes { get; set; }
        }

        [Preserve]
        private class BrowerSdkConfig
        {
            [Preserve]
            public string applicationId { get; set; }
            [Preserve]
            public string clientToken { get; set; }
            [Preserve]
            public string site { get; set; }
            [Preserve]
            public float sessionSampleRate { get; set; }
            [Preserve]
            public float sessionReplaySampleRate { get; set; } = 0f;
            [Preserve]
            public string service { get; set; }
            [Preserve]
            public string env { get; set; }
            [Preserve]
            public string version { get; set; }
            [Preserve]
            public string proxy { get; set; }
            [Preserve]
            public List<AllowedTracingUrl> allowedTracingUrls { get; set; }
            [Preserve]
            public float traceSampleRate { get; set; }
            [Preserve]
            public string traceContextInjection { get; set; }
            [Preserve]
            public bool trackFrustrations { get; set; }
            [Preserve]
            public bool trackUserInteractions { get; set; }
            [Preserve]
            public bool trackViewsManually { get; set; } = true;
            [Preserve]
            public bool trackResources { get; set; }
            [Preserve]
            public bool trackLongTasks { get; set; }
            [Preserve]
            public string source { get; set; }
            [Preserve]
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public object trackResourceHeaders { get; set; }
        }

#pragma warning restore SA1310
#pragma warning restore SA1307

        [DllImport("__Internal")]
        private static extern bool DDRum_InitRum(string configurationJson);

        [DllImport("__Internal")]
        private static extern void DDRum_AddAttribute(string attributeJson);

        [DllImport("__Internal")]
        private static extern void DDRum_RemoveAttribute(string name);

        [DllImport("__Internal")]
        private static extern void DDRum_AddTiming(string name);

        [DllImport("__Internal")]
        private static extern void DDRum_AddAction(string type, string name, string attributeJson);

        [DllImport("__Internal")]
        private static extern void DDRum_AddError(string errorKind, string errorMessage, string errorStackTrace, string errorSource, string attributeJson);

        [DllImport("__Internal")]
        private static extern void DDRum_StartResource(string key, string httpMethod, string url, string attributeJson);

        [DllImport("__Internal")]
        private static extern void DDRum_StopResource(string url, string optionsJson);

        [DllImport("__Internal")]
        private static extern void DDRum_AddResourceError(string method, string url, string errorKind,
            string errorMessage, string errorStackTrace, string attributes);

        [DllImport("__Internal")]
        private static extern void DDRum_AddFeatureFlagEvaluation(string attributeJson);

        [DllImport("__Internal")]
        private static extern void DDRum_StartView(string viewName, string attributesJson);

        [DllImport("__Internal")]
        private static extern void DDRum_StopSession();
    }
}
