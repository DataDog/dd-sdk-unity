// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
        // The DateProvider is needed here to get timestamps in nanoseconds for resource tracking. It can
        // be used in Web safely only because web does not have multithreading.
        private readonly IDateProvider _dateProvider;
        private readonly ResourceTracker _resourceTracker = new ();

        public DatadogWebGLRum(IDateProvider dateProvider = null)
        {
            _dateProvider = dateProvider ?? new DefaultDateProvider();
        }

        public void Init(DatadogConfigurationOptions options)
        {
            var allowedTracingUrls = options.FirstPartyHosts.Select((e) =>
            {
                // Match http and https, and any subdomains (consistent with other platforms)
                var hostRegex = $@"^http[s]?:\/\/(.*\.)*{Regex.Escape(e.Host)}\/";
                return new AllowedTracingUrl()
                {
                    match = e.Host,
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
                // TODO: Version
                version = "unknown",
                traceSampleRate = options.TraceSampleRate,
                trackFrustrations = true,
                trackResources = true,
                trackLongTasks = true,
                proxy = string.IsNullOrEmpty(options.CustomEndpoint) ? null : options.CustomEndpoint,
                allowedTracingUrls = allowedTracingUrls,
                traceContextInjection = options.TraceContextInjection.ToWebValue(),
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
            // Pull out the timestamp from attributes
            var timestamp = _dateProvider.Now;
            _resourceTracker.StartResource(timestamp, key, httpMethod, url, attributes);
        }

        public void StopResource(
            string key,
            RumResourceType kind,
            int? statusCode = null,
            long? size = null,
            Dictionary<string, object> attributes = null)
        {
            var timestamp = _dateProvider.Now;
            var resourceInfo = _resourceTracker.StopResource(timestamp, key, kind, statusCode, size, attributes);
            if (resourceInfo == null)
            {
                return;
            }

            var uuid = Guid.NewGuid().ToString();

            var dd = ExtractDdData(resourceInfo.Attributes);

            // Create and serialize the RUM resource event.
            // Because longs can't be sent to JavaScript easily, we put them in the attributes
            // to be serialized as part of the JSON.
            var date = new DateTimeOffset(timestamp).ToUnixTimeMilliseconds();
            resourceInfo.Attributes[DdRumProcessor.DdRumTimestampAttribute] = date;

            // Ticks are 100 ns each by standard .NET definition. This is the best resolution we can get
            // prior to .NET 7 which adds DateTime.TotalNanoseconds.
            var durationNs = (resourceInfo.StopTimestamp - resourceInfo.StartTimestamp).Value.Ticks * 100;
            var resourceEvent = new WebResourceEvent()
            {
                date = date,
                resource = new ()
                {
                    id = uuid,
                    type = resourceInfo.Kind.ToWebValue(),
                    url = resourceInfo.Url,
                    duration = durationNs,
                    method = resourceInfo.Method.ToWebValue(),
                    status_code = resourceInfo.StatusCode,
                    size = resourceInfo.Size,
                },
                dd = dd,
                context = resourceInfo.Attributes,
            };
            var resourceEventJson = JsonConvert.SerializeObject(resourceEvent);

            DDRum_AddResource(resourceEventJson);
        }

        public void StopResourceWithError(string key, string errorType, string errorMessage, Dictionary<string, object> attributes = null)
        {
            StopResourceWithError(key, new ErrorInfo(errorType, errorMessage), attributes);
        }

        public void StopResource(string key, Exception error, Dictionary<string, object> attributes = null)
        {
            StopResourceWithError(key, new ErrorInfo(error), attributes);
        }

        public void StopResourceWithError(string key, ErrorInfo error, Dictionary<string, object> attributes = null)
        {
            var timestamp = _dateProvider.Now;
            var resourceInfo = _resourceTracker.StopResourceWithError(timestamp, key, error.Type, error.Message, attributes);
            if (resourceInfo == null)
            {
                return;
            }

            var attributesJson = JsonConvert.SerializeObject(resourceInfo.Attributes);
            DDRum_AddResourceError(resourceInfo.Method.ToWebValue(), resourceInfo.Url, resourceInfo.ErrorType, resourceInfo.ErrorMessage, error.StackTrace, attributesJson);
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

        private WebResourceEventDdData ExtractDdData(Dictionary<string, object> attributes)
        {
            if (attributes == null)
            {
                return null;
            }

            attributes.TryGetValue(ResourceTrackingHelper.DatadogAttributeKeys.TraceId, out var traceId);
            attributes.Remove(ResourceTrackingHelper.DatadogAttributeKeys.TraceId);
            attributes.TryGetValue(ResourceTrackingHelper.DatadogAttributeKeys.SpanId, out var spanId);
            attributes.Remove(ResourceTrackingHelper.DatadogAttributeKeys.SpanId);
            attributes.TryGetValue(ResourceTrackingHelper.DatadogAttributeKeys.RulePsr, out var rulePsr);
            attributes.Remove(ResourceTrackingHelper.DatadogAttributeKeys.RulePsr);

            return new ()
            {
                trace_id = traceId as string,
                span_id = spanId as string,
                rule_psr = (float?)rulePsr,
                discarded = false,
            };
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
            public bool trackViewsManually { get; set; } = true;
            [Preserve]
            public bool trackResources { get; set; }
            [Preserve]
            public bool trackLongTasks { get; set; }
            [Preserve]
            public List<string> enableExperimentalFeatures { get; set; } = new List<string>();
        }

        [Preserve]
        private class WebResourceEvent
        {
            [Preserve]
            public long date { get; set; }
            [Preserve]
            public string type { get; set; } = "resource";
            [Preserve]
            public WebResourceEventData resource { get; set; }
            [Preserve]
            public Dictionary<string, object> context { get; set; }
            [Preserve]
            public WebResourceEventDdData dd { get; set; }
        }

        [Preserve]
        private class WebResourceEventDdData
        {
            [Preserve]
            public string trace_id { get; set; }
            [Preserve]
            public string span_id { get; set; }
            [Preserve]
            public float? rule_psr { get; set; }
            [Preserve]
            public bool discarded { get; set; }
        }

        [Preserve]
        private class WebResourceEventData
        {
            [Preserve]
            public string id { get; set; }
            [Preserve]
            public string type { get; set; }
            [Preserve]
            public string url { get; set; }
            [Preserve]
            public long duration { get; set; }
            [Preserve]
            public string method { get; set; }
            [Preserve]
            public int? status_code { get; set; }
            [Preserve]
            public long? size { get; set; }
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
        private static extern void DDRum_AddResource(string resourceEventJson);

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
