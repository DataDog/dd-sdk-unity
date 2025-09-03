// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;

namespace Datadog.Unity.Rum
{
    /// <summary>
    /// Context required to propagate tracing details for an outgoing HTTP request. Indicates that
    /// the request is initiated within the given span (which may be a child of another span), and
    /// that said span is part of a given trace.
    /// </summary>
    public class TraceContext
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

        /// <summary>
        /// Mutates the provided <c>headers</c> dictionary, adding the header values necessary to
        /// inject the given tracing context into an outgoing HTTP request in the desired
        /// format(s).
        /// </summary>
        public void InjectHeaders(
            Dictionary<string, string> headers,
            TracingHeaderType tracingHeaderType,
            TraceContextInjection contextInjection)
        {
            // If the SDK is configured to only inject trace context when an explicit, affirmative
            // sampling decision has been made, return early and inject no headers
            if (!sampled && contextInjection == TraceContextInjection.Sampled)
            {
                return;
            }

            // If desired, inject 'x-datadog-*' headers
            if ((tracingHeaderType & TracingHeaderType.Datadog) != 0)
            {
                InjectDatadogHeaders(headers);
            }

            // If desired, inject OpenTelemetry B3 context in single-header encoding
            if ((tracingHeaderType & TracingHeaderType.B3) != 0)
            {
                InjectB3Headers(headers);
            }

            // If desired, inject OpenTelemetry B3 context in multi-header encoding
            if ((tracingHeaderType & TracingHeaderType.B3Multi) != 0)
            {
                InjectB3MultiHeaders(headers);
            }

            // If desired, inject W3C Trace Context headers
            if((tracingHeaderType & TracingHeaderType.TraceContext) != 0)
            {
                InjectW3CTraceContextHeaders(headers);
            }
        }

        private void InjectDatadogHeaders(Dictionary<string, string> headers)
        {
            var traceIdString = traceId.ToString(TraceIdRepresentation.LowDec);
            var traceIdTagString = traceId.ToString(TraceIdRepresentation.HighHex16Chars);
            var spanIdString = spanId.ToString(TraceIdRepresentation.Dec);

            headers[DatadogHttpTracingHeaders.TraceId] = traceIdString;
            headers[DatadogHttpTracingHeaders.Tags] = $"{DatadogHttpTracingHeaders.TraceIdTag}={traceIdTagString}";
            headers[DatadogHttpTracingHeaders.ParentId] = spanIdString; // x-datadog-parent-id specifies *current* span ID
            headers[DatadogHttpTracingHeaders.Origin] = "rum";
            headers[DatadogHttpTracingHeaders.SamplingPriority] = sampled ? "1" : "0";
        }

        private void InjectB3Headers(Dictionary<string, string> headers)
        {
            // If this request was not sampled, but we're configured to inject tracing context
            // regardless, inject "0" (indicating "this request was not sampled") as our context
            if (!sampled)
            {
                headers[OTelHttpTracingHeaders.SingleB3] = "0";
                return;
            }

            // Otherwise, inject full B3 context as a single header value: concatenate trace ID,
            // span ID, and '1' to indicate that the request was sampled, delimited by '-'
            var traceIdString = traceId.ToString(TraceIdRepresentation.Hex32Chars);
            var spanIdString = spanId.ToString(TraceIdRepresentation.Hex16Chars);
            var headerValue = $"{traceIdString}-{spanIdString}-1";

            // If we have a parent span ID, append it
            if (parentSpanId != null)
            {
                var parentSpanIdString = parentSpanId.Value.ToString(TraceIdRepresentation.Hex16Chars);
                headerValue += $"-{parentSpanIdString}";
            }

            // Inject our 'b3' header value
            headers[OTelHttpTracingHeaders.SingleB3] = headerValue;
        }

        private void InjectB3MultiHeaders(Dictionary<string, string> headers)
        {
            // If this request wasn't sampled, simply inject 'X-B3-Sampled: 0'
            if (!sampled)
            {
                headers[OTelHttpTracingHeaders.MultipleSampled] = "0";
                return;
            }

            // Otherwise, inject the requisite context as individual B3 header values
            headers[OTelHttpTracingHeaders.MultipleSampled] = "1";

            var traceIdString = traceId.ToString(TraceIdRepresentation.Hex32Chars);
            headers[OTelHttpTracingHeaders.MultipleTraceId] = traceIdString;

            var spanIdString = spanId.ToString(TraceIdRepresentation.Hex16Chars);
            headers[OTelHttpTracingHeaders.MultipleSpanId] = spanIdString;

            if (parentSpanId != null)
            {
                var parentSpanIdString = parentSpanId.Value.ToString(TraceIdRepresentation.Hex16Chars);
                headers[OTelHttpTracingHeaders.MultipleParentId] = parentSpanIdString;
            }
        }

        private void InjectW3CTraceContextHeaders(Dictionary<string, string> headers)
        {
            var traceIdString = traceId.ToString(TraceIdRepresentation.Hex32Chars);
            var spanIdString = spanId.ToString(TraceIdRepresentation.Hex16Chars);
            var sampledString = sampled ? "1" : "0";
            var tcSampledString = sampled ? "01" : "00";
            headers[W3CTracingHeaders.TraceParent] = $"00-{traceIdString}-{spanIdString}-{tcSampledString}";
            headers[W3CTracingHeaders.TraceState] = $"dd=s:{sampledString};o:rum;p:{spanIdString}";
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
}
