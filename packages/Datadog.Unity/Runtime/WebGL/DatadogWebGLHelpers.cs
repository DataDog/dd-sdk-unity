// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Rum;

namespace Datadog.Unity.WebGL
{
    internal static class DatadogWebGLHelpers
    {
        internal static string ToWebValue(this DatadogSite site)
        {
            return site switch
            {
                DatadogSite.Us1 => "datadoghq.com",
                DatadogSite.Us3 => "us3.datadoghq.com",
                DatadogSite.Us5 => "us5.datadoghq.com",
                DatadogSite.Eu1 => "datadoghq.eu",
                DatadogSite.Us1Fed => "ddog-gov.com",
                DatadogSite.Ap1 => "ap1.datadoghq.com",
                DatadogSite.Ap2 => "ap2.datadoghq.com",
                _ => "datadoghq.com"
            };
        }

        internal static string ToWebValue(this TrackingConsent consent)
        {
            return consent switch
            {
                TrackingConsent.NotGranted => "not-granted",
                TrackingConsent.Granted => "granted",
                _ => "not-granted"
            };
        }

        internal static List<string> ToWebValue(this TracingHeaderType headerType)
        {
            var result = new List<string>();
            if ((headerType & TracingHeaderType.Datadog) != 0)
            {
                result.Add("datadog");
            }

            if ((headerType & TracingHeaderType.TraceContext) != 0)
            {
                result.Add("tracecontext");
            }

            if ((headerType & TracingHeaderType.B3) != 0)
            {
                result.Add("b3");
            }

            if ((headerType & TracingHeaderType.B3Multi) != 0)
            {
                result.Add("b3multi");
            }

            return result;
        }

        internal static string ToWebValue(this TraceContextInjection contextInjection)
        {
            return contextInjection switch
            {
                TraceContextInjection.All => "all",
                _ => "sampled"
            };
        }

        internal static string ToWebValue(this RumUserActionType actionType)
        {
            return actionType switch
            {
                RumUserActionType.Tap => "tap",
                RumUserActionType.Scroll => "scroll",
                RumUserActionType.Swipe => "swipe",
                _ => "custom"
            };
        }

        internal static string ToWebValue(this RumResourceType resourceType)
        {
            return resourceType switch
            {
                RumResourceType.Image => "image",
                RumResourceType.Xhr => "xhr",
                RumResourceType.Beacon => "beacon",
                RumResourceType.Fetch => "fetch",
                RumResourceType.Media => "media",
                RumResourceType.Font => "font",
                RumResourceType.Document => "document",
                RumResourceType.Css => "css",
                RumResourceType.Js => "js",
                RumResourceType.Native => "native",
                _ => "other",
            };
        }

        internal static string ToWebValue(this RumHttpMethod method)
        {
            return method switch
            {
                RumHttpMethod.Get => "GET",
                RumHttpMethod.Post => "POST",
                RumHttpMethod.Put => "PUT",
                RumHttpMethod.Delete => "DELETE",
                RumHttpMethod.Head => "HEAD",
                RumHttpMethod.Patch => "PATCH",
                _ => "get",
            };
        }

        internal static string ToWebValue(this RumErrorSource source)
        {
            return source switch
            {
                RumErrorSource.Source => "source",
                RumErrorSource.Network => "network",
                RumErrorSource.WebView => "webview",
                RumErrorSource.Console => "console",
                _ => "custom",
            };
        }
    }
}
