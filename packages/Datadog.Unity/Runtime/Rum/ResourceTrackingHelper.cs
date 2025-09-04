// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Datadog.Unity.Rum
{
    /// <summary>
    /// Contains the core logic for generating trace contexts, determining which hosts should
    /// receive tracing headers, and injecting various tracing header formats (Datadog, B3, W3C
    /// TraceContext).
    ///
    /// Under normal circumstances, using <see cref="DatadogTrackedWebRequest"/> is sufficient to
    /// ensure that your application's web requests will be tracked as RUM resources with the
    /// appropriate trace context injected. If you use third-party HTTP client, or you're otherwise
    /// unable to use DatadogTrackedWebRequest, you may handle resource tracking and context
    /// injection manually by using this class's public functions to manipulate outgoing requests.
    /// </summary>
    public class ResourceTrackingHelper
    {
        private readonly DatadogConfigurationOptions _options;
        private readonly RateBasedSampler _traceSampler;
        private readonly List<FirstPartyHost> _firstPartyHosts;
        private readonly TraceContextInjection _traceContextInjection;

        internal ResourceTrackingHelper(DatadogConfigurationOptions options)
        {
            _options = options;
            _traceSampler = new RateBasedSampler(options.TraceSampleRate / 100.0f);
            _firstPartyHosts = options.FirstPartyHosts
                .Select(x => new FirstPartyHost(x.Host, x.TracingHeaderType))
                .ToList();
            _traceContextInjection = options.TraceContextInjection;
        }

        /// <summary>
        /// Generates a new <see cref="TraceContext"/> for an outgoing HTTP request.
        /// </summary>
        public TraceContext GenerateTraceContext()
        {
            return new TraceContext(
                TracingUuid.Create128Bit(),
                TracingUuid.Create63Bit(),
                null,
                _traceSampler.Sample());
        }

        /// <summary>
        /// Returns the set of internal '_dd' attributes that should be supplied when creating a
        /// RUM resource for a request with the given trace context.
        /// </summary>
        public Dictionary<string, object> GenerateDatadogAttributes(TraceContext traceContext)
        {
            var attributes = new Dictionary<string, object>();
            attributes[DatadogAttributeKeys.RulePsr] = _options.TraceSampleRate / 100.0f;
            if (traceContext.sampled)
            {
                attributes[DatadogAttributeKeys.TraceId] = traceContext.traceId.ToString(TraceIdRepresentation.Hex32Chars);
                attributes[DatadogAttributeKeys.SpanId] = traceContext.spanId.ToString(TraceIdRepresentation.Dec);
            }

            return attributes;
        }

        /// <summary>
        /// Given the URL for an outgoing HTTP request, returns a bit field value indicating the
        /// set of header formats to be used when injecting trace context into that request, or
        /// None if no trace context should be injected.
        ///
        /// In order for a request to be considered for trace context injection, its URL must match
        /// one of the "first-party host" values configured in the project's Datadog settings.
        /// </summary>
        public TracingHeaderType HeaderTypesForHost(Uri request)
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

        /// <summary>
        /// Returns the header values that should be set on an outgoing HTTP request in order to
        /// inject the given trace context in the desired format(s).
        /// </summary>
        public Dictionary<string, string> GenerateTracingHeaders(TraceContext traceContext, TracingHeaderType tracingHeaderType)
        {
            var headers = new Dictionary<string, string>();
            traceContext.InjectHeaders(headers, tracingHeaderType, _traceContextInjection);
            return headers;
        }

        public static class DatadogAttributeKeys
        {
            public const string TraceId = "_dd.trace_id";
            public const string SpanId = "_dd.span_id";
            public const string RulePsr = "_dd.rule_psr";
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
            // If the port is not the default port, it should be included in the regex and therefore
            // should be included in the match request
            if (!uri.IsDefaultPort)
            {
                return _regex.IsMatch($"{uri.Host}:{uri.Port}");
            }

            return _regex.IsMatch(uri.Host);
        }
    }
}
