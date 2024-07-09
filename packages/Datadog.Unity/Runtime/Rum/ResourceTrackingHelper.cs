// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Datadog.Unity.Rum
{
    // This is separated so it can be tested as its own unit, as
    // well as bypassing the DDRum proxy.
    internal class ResourceTrackingHelper
    {
        private readonly DatadogConfigurationOptions _options;
        private readonly RateBasedSampler _traceSampler;
        private readonly List<FirstPartyHost> _firstPartyHosts;
        private readonly TraceContextInjection _traceContextInjection;

        public TraceContextInjection TraceContextInjection => _traceContextInjection;

        public ResourceTrackingHelper(DatadogConfigurationOptions options)
        {
            _options = options;
            _traceSampler = new RateBasedSampler(options.TraceSampleRate / 100.0f);
            _firstPartyHosts = options.FirstPartyHosts
                .Select(x => new FirstPartyHost(x.Host, x.TracingHeaderType))
                .ToList();
            _traceContextInjection = options.TraceContextInjection;
        }

        public TraceContext GenerateTraceContext()
        {
            return new TraceContext(
                TracingUuid.Create128Bit(),
                TracingUuid.Create63Bit(),
                null,
                _traceSampler.Sample());
        }

        internal void GenerateDatadogAttributes(TraceContext traceContext, Dictionary<string, object> attributes)
        {
            attributes[DatadogAttributeKeys.RulePsr] = _options.TraceSampleRate / 100.0f;
            if (traceContext.sampled)
            {
                attributes[DatadogAttributeKeys.TraceId] = traceContext.traceId.ToString(TraceIdRepresentation.Hex32Chars);
                attributes[DatadogAttributeKeys.SpanId] = traceContext.spanId.ToString(TraceIdRepresentation.Dec);
            }
        }

        internal TracingHeaderType HeaderTypesForHost(Uri request)
        {
            foreach (var host in _firstPartyHosts)
            {
                if (host.IsMatch(request))
                {
                    return host.headerTypes;
                }
            }

            return TracingHeaderType.None;
        }

        internal void GenerateTracingHeaders(
            TraceContext traceContext,
            TracingHeaderType tracingHeaderType,
            TraceContextInjection contextInjection,
            Dictionary<string, string> headers)
        {
            if (!traceContext.sampled && contextInjection == TraceContextInjection.Sampled)
            {
                // Easy out. Don't add any headers if the context is not sampled and we're only injecting sampled.
                return;
            }

            if ((tracingHeaderType & TracingHeaderType.Datadog) != 0)
            {
                InjectDatadogHeaders(traceContext, headers);
            }

            if ((tracingHeaderType & TracingHeaderType.B3) != 0)
            {
                InjectB3Headers(traceContext, headers);
            }

            if ((tracingHeaderType & TracingHeaderType.B3Multi) != 0)
            {
                InjectB3MultiHeaders(traceContext, headers);
            }

            if((tracingHeaderType & TracingHeaderType.TraceContext) != 0)
            {
                InjectTraceContextHeaders(traceContext, headers);
            }
        }

        private static void InjectTraceContextHeaders(TraceContext traceContext, Dictionary<string, string> headers)
        {
            var sampledString = traceContext.sampled ? "1" : "0";
            var spanString = traceContext.spanId.ToString(TraceIdRepresentation.Hex16Chars);
            var traceString = traceContext.traceId.ToString(TraceIdRepresentation.Hex32Chars);
            var tcSampledString = traceContext.sampled ? "01" : "00";
            headers[W3CTracingHeaders.TraceParent] = $"00-{traceString}-{spanString}-{tcSampledString}";
            headers[W3CTracingHeaders.TraceState] = $"dd=s:{sampledString};o:rum;p:{spanString}";
        }

        private static void InjectB3MultiHeaders(TraceContext traceContext, Dictionary<string, string> headers)
        {
            headers[OTelHttpTracingHeaders.MultipleSampled] = traceContext.sampled ? "1" : "0";;
            if (traceContext.sampled)
            {
                headers[OTelHttpTracingHeaders.MultipleTraceId] =
                    traceContext.traceId.ToString(TraceIdRepresentation.Hex32Chars);
                headers[OTelHttpTracingHeaders.MultipleSpanId] =
                    traceContext.spanId.ToString(TraceIdRepresentation.Hex16Chars);
                if (traceContext.parentSpanId != null)
                {
                    headers[OTelHttpTracingHeaders.MultipleParentId] =
                        traceContext.parentSpanId.Value.ToString(TraceIdRepresentation.Hex16Chars);
                }
            }
        }

        private static void InjectB3Headers(TraceContext traceContext, Dictionary<string, string> headers)
        {
            var sampledString = traceContext.sampled ? "1" : "0";
            if (traceContext.sampled)
            {
                var traceString = traceContext.traceId.ToString(TraceIdRepresentation.Hex32Chars);
                var spanString = traceContext.spanId.ToString(TraceIdRepresentation.Hex16Chars);
                var headerValue = $"{traceString}-{spanString}-{sampledString}";
                if (traceContext.parentSpanId != null)
                {
                    headerValue +=
                        $"-{traceContext.parentSpanId.Value.ToString(TraceIdRepresentation.Hex16Chars)}";
                }

                headers[OTelHttpTracingHeaders.SingleB3] = headerValue;
            }
            else
            {
                headers[OTelHttpTracingHeaders.SingleB3] = sampledString;
            }
        }

        private void InjectDatadogHeaders(
            TraceContext traceContext,
            Dictionary<string, string> headers)
        {
            headers[DatadogHttpTracingHeaders.TraceId] =
                traceContext.traceId.ToString(TraceIdRepresentation.LowDec);
            headers[DatadogHttpTracingHeaders.Tags] =
                $"{DatadogHttpTracingHeaders.TraceIdTag}={traceContext.traceId.ToString(TraceIdRepresentation.HighHex16Chars)}";
            headers[DatadogHttpTracingHeaders.ParentId] =
                traceContext.spanId.ToString(TraceIdRepresentation.Dec);
            headers[DatadogHttpTracingHeaders.Origin] = "rum";
            headers[DatadogHttpTracingHeaders.SamplingPriority] = traceContext.sampled ? "1" : "0";
        }

        private static class DatadogAttributeKeys
        {
            public const string TraceId = "_dd.trace_id";
            public const string SpanId = "_dd.span_id";
            public const string RulePsr = "_dd.rule_psr";
        }

        private static class DatadogHttpTracingHeaders
        {
            public const string TraceId = "x-datadog-trace-id";
            public const string ParentId = "x-datadog-parent-id";
            public const string SamplingPriority = "x-datadog-sampling-priority";
            public const string Origin = "x-datadog-origin";
            public const string Tags = "x-datadog-tags";

            public const string TraceIdTag = "_dd.p.tid";
        }

        private static class OTelHttpTracingHeaders
        {
            public const string MultipleTraceId = "X-B3-TraceId";
            public const string MultipleSpanId = "X-B3-SpanId";
            public const string MultipleParentId = "X-B3-ParentId";
            public const string MultipleSampled = "X-B3-Sampled";

            public const string SingleB3 = "b3";
        }

        private static class W3CTracingHeaders
        {
            public const string TraceParent = "traceparent";
            public const string TraceState = "tracestate";
        }
    }

    internal class TraceContext
    {
        public readonly TracingUuid traceId;
        public readonly TracingUuid spanId;
        public readonly TracingUuid? parentSpanId;
        public readonly bool sampled;

        public TraceContext(TracingUuid traceId, TracingUuid spanId, TracingUuid? parentSpanId, bool sampled)
        {
            this.traceId = traceId;
            this.spanId = spanId;
            this.parentSpanId = parentSpanId;
            this.sampled = sampled;
        }
    }

    internal class FirstPartyHost
    {
        private readonly Regex _regex;

        private readonly TracingHeaderType _headerTypes;
        public TracingHeaderType headerTypes => _headerTypes;

        public FirstPartyHost(string host, TracingHeaderType headerTypes)
        {
            _regex = new Regex($"^(.*\\.)*{Regex.Escape(host)}$");
            _headerTypes = headerTypes;
        }

        public bool IsMatch(Uri uri)
        {
            return _regex.IsMatch(uri.Host);
        }
    }
}
