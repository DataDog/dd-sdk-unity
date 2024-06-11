// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using NUnit.Framework;

namespace Datadog.Unity.Rum.Tests
{
    [TestFixture(true)]
    [TestFixture(false)]
    public class DatadogTrackedWebRequestTest
    {
        private bool _sampled;
        private ResourceTrackingHelper _trackingHelper;

        public DatadogTrackedWebRequestTest(bool sampled)
        {
            _sampled = sampled;
        }

        [SetUp]
        public void SetUp()
        {
            var options = new DatadogConfigurationOptions()
            {
                TraceSampleRate = _sampled ? 100 : 0.0f,
            };
            _trackingHelper = new ResourceTrackingHelper(options);
        }

        [Test]
        public void GeneratesCorrectDatadogHeaders()
        {
            // Given
            var headers = new Dictionary<string, string>();
            var context = _trackingHelper.GenerateTraceContext();

            // When
            _trackingHelper.GenerateTracingHeaders(context, TracingHeaderType.Datadog, headers);

            // Then
            VerifyHeaders(headers, TracingHeaderType.Datadog, _sampled);
        }

        [Test]
        public void GeneratesCorrectB3Headers()
        {
            // Given
            var headers = new Dictionary<string, string>();
            var context = _trackingHelper.GenerateTraceContext();

            // When
            _trackingHelper.GenerateTracingHeaders(context, TracingHeaderType.B3, headers);

            // Then
            VerifyHeaders(headers, TracingHeaderType.B3, _sampled);
        }

        [Test]
        public void GeneratesCorrectB3MultiHeaders()
        {
            // Given
            var headers = new Dictionary<string, string>();
            var context = _trackingHelper.GenerateTraceContext();

            // When
            _trackingHelper.GenerateTracingHeaders(context, TracingHeaderType.B3Multi, headers);

            // Then
            VerifyHeaders(headers, TracingHeaderType.B3Multi, _sampled);
        }

        [Test]
        public void GeneratesCorrectTraceContextHeaders()
        {
            // Given
            var headers = new Dictionary<string, string>();
            var context = _trackingHelper.GenerateTraceContext();

            // When
            _trackingHelper.GenerateTracingHeaders(context, TracingHeaderType.TraceContext, headers);

            // Then
            VerifyHeaders(headers, TracingHeaderType.TraceContext, _sampled);
        }

        [Test]
        public void GeneratesCorrectDatadogAttributesWhenSampled()
        {
            // Given
            var attributes = new Dictionary<string, object>();
            var context = _trackingHelper.GenerateTraceContext();

            // When
            _trackingHelper.GenerateDatadogAttributes(context, attributes);

            // Then
            var traceString = attributes.GetValueOrDefault("_dd.trace_id")?.ToString();
            var spanString = attributes.GetValueOrDefault("_dd.span_id")?.ToString();
            if (_sampled)
            {
                Assert.IsNotNull(traceString);
                BigInteger.TryParse(traceString, NumberStyles.HexNumber, null, out var traceId);
                Assert.IsNotNull(traceId);
                Assert.LessOrEqual(traceId.GetByteCount(), 128);

                Assert.IsNotNull(spanString);
                BigInteger.TryParse(spanString, NumberStyles.HexNumber, null, out var spanId);
                Assert.IsNotNull(spanId);
                Assert.LessOrEqual(spanId.GetByteCount(), 63);
            }
            else
            {
                Assert.IsNull(traceString);
                Assert.IsNull(spanString);
            }

            Assert.AreEqual(_sampled ? 1.0f : 0.0f, attributes["_dd.rule_psr"]);
        }

        [TestCase(TracingHeaderType.Datadog)]
        [TestCase(TracingHeaderType.B3)]
        [TestCase(TracingHeaderType.B3Multi)]
        [TestCase(TracingHeaderType.TraceContext)]
        public void DatadogAttributesAndTracingHeadersHaveSameValue(TracingHeaderType headerType)
        {
            // Given
            var headers = new Dictionary<string, string>();
            var attributes = new Dictionary<string, object>();
            var context = _trackingHelper.GenerateTraceContext();

            // When
            _trackingHelper.GenerateDatadogAttributes(context, attributes);
            _trackingHelper.GenerateTracingHeaders(context, headerType, headers);

            // Then
            var traceString = attributes.GetValueOrDefault("_dd.trace_id")?.ToString();
            var spanString = attributes.GetValueOrDefault("_dd.span_id")?.ToString();
            if (_sampled)
            {
                Assert.IsNotNull(traceString);
                BigInteger.TryParse(traceString, NumberStyles.HexNumber, null, out var attributeTraceId);

                Assert.IsNotNull(spanString);
                BigInteger.TryParse(spanString, out var attributeSpanId);

                GetIdsFromHeaders(headers, headerType, out var headerTraceId, out var headerSpanId);
                Assert.AreEqual(headerTraceId, attributeTraceId);
                Assert.AreEqual(context.traceId.ToString(TraceIdRepresentation.Hex), traceString);
                Assert.AreEqual(headerSpanId, attributeSpanId);
                Assert.AreEqual(context.spanId.ToString(TraceIdRepresentation.Dec), spanString);
            }
            else
            {
                Assert.IsNull(traceString);
                Assert.IsNull(spanString);
            }

            Assert.AreEqual(_sampled ? 1.0f : 0.0f, attributes["_dd.rule_psr"]);
        }

        private void GetIdsFromHeaders(Dictionary<string, string> headers, TracingHeaderType headerType, out BigInteger traceId, out BigInteger spanId)
        {
            switch (headerType)
            {
                case TracingHeaderType.Datadog:
                {
                    BigInteger.TryParse(headers["x-datadog-trace-id"], out traceId);
                    BigInteger.TryParse(headers["x-datadog-parent-id"], out spanId);
                    var tagsHeader = headers.GetValueOrDefault("x-datadog-tags");
                    var tagParts = tagsHeader?.Split("=");
                    BigInteger.TryParse(tagParts[1], NumberStyles.HexNumber, null, out var highTraceId);
                    highTraceId <<= 64;
                    traceId += highTraceId;
                    break;
                }

                case TracingHeaderType.B3:
                {
                    var header = headers["b3"];
                    var headerParts = header.Split("-");
                    BigInteger.TryParse(headerParts[0], NumberStyles.HexNumber, null, out traceId);
                    BigInteger.TryParse(headerParts[1], NumberStyles.HexNumber, null, out spanId);
                    break;
                }

                case TracingHeaderType.B3Multi:
                {
                    BigInteger.TryParse(headers["X-B3-TraceId"], NumberStyles.HexNumber, null, out traceId);
                    BigInteger.TryParse(headers["X-B3-SpanId"], NumberStyles.HexNumber, null, out spanId);
                    break;
                }

                case TracingHeaderType.TraceContext:
                {
                    var traceparent = headers["traceparent"];
                    var headerParts = traceparent.Split("-");
                    BigInteger.TryParse(headerParts[1], NumberStyles.HexNumber, null, out traceId);
                    BigInteger.TryParse(headerParts[2], NumberStyles.HexNumber, null, out spanId);
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(headerType), headerType, null);
            }
        }

        private static void VerifyHeaders(Dictionary<string, string> headers, TracingHeaderType tracingHeaderType, bool sampled)
        {
            BigInteger traceInt;
            BigInteger spanInt;
            string sampleString = sampled ? "1" : "0";
            switch (tracingHeaderType)
            {
                case TracingHeaderType.Datadog:
                {
                    Assert.AreEqual(sampleString, headers["x-datadog-sampling-priority"]);
                    Assert.AreEqual("rum", headers["x-datadog-origin"]);
                    var traceString = headers.GetValueOrDefault("x-datadog-trace-id");
                    var spanString = headers.GetValueOrDefault("x-datadog-parent-id");
                    var tagsHeader = headers.GetValueOrDefault("x-datadog-tags");
                    var tagParts = tagsHeader?.Split("=");
                    if (sampled)
                    {
                        Assert.IsNotNull(tagParts);
                        if (tagParts != null)
                        {
                            Assert.AreEqual(tagParts[0], "_dd.p.tid");
                            BigInteger.TryParse(tagParts[1], NumberStyles.HexNumber, null, out var highTraceInt);
                            Assert.NotNull(highTraceInt);
                            Assert.LessOrEqual(highTraceInt.GetByteCount(), 64);
                        }

                        BigInteger.TryParse(traceString, out traceInt);
                        BigInteger.TryParse(spanString, out spanInt);
                    }
                    else
                    {
                        Assert.IsNull(traceString);
                        Assert.IsNull(spanString);
                    }

                    break;
                }

                case TracingHeaderType.B3:
                {
                    var header = headers["b3"];
                    if (sampled)
                    {
                        var headerParts = header.Split("-");
                        BigInteger.TryParse(headerParts[0], NumberStyles.HexNumber, null, out traceInt);
                        BigInteger.TryParse(headerParts[1], NumberStyles.HexNumber, null, out spanInt);
                        Assert.AreEqual(32, headerParts[0].Length);
                        Assert.AreEqual(16, headerParts[1].Length);
                        Assert.AreEqual(headerParts[2], sampleString);
                    }
                    else
                    {
                        Assert.AreEqual("0", header);
                    }

                    break;
                }

                case TracingHeaderType.B3Multi:
                {
                    Assert.AreEqual(sampleString, headers["X-B3-Sampled"]);
                    var traceString = headers.GetValueOrDefault("X-B3-TraceId");
                    var spanString = headers.GetValueOrDefault("X-B3-SpanId");
                    if (sampled)
                    {
                        BigInteger.TryParse(traceString, NumberStyles.HexNumber, null,
                            out traceInt);
                        Assert.AreEqual(32, traceString.Length);
                        BigInteger.TryParse(spanString, NumberStyles.HexNumber, null,
                            out traceInt);
                        Assert.AreEqual(16, spanString.Length);
                    }
                    else
                    {
                        Assert.IsNull(traceString);
                        Assert.IsNull(spanString);
                    }

                    break;
                }

                case TracingHeaderType.TraceContext:
                {
                    var traceparent = headers["traceparent"];
                    var headerParts = traceparent.Split("-");
                    Assert.AreEqual("00", headerParts[0]);
                    BigInteger.TryParse(headerParts[1], NumberStyles.HexNumber, null, out traceInt);
                    BigInteger.TryParse(headerParts[2], NumberStyles.HexNumber, null, out spanInt);
                    Assert.AreEqual(sampled ? "01" : "00", headerParts[3]);

                    var tracestate = headers["tracestate"];
                    var ddTraceState = tracestate.Split(",").FirstOrDefault(s => s.StartsWith("dd="))?.Substring(3);
                    Assert.NotNull(ddTraceState);
                    var traceStateMap = ddTraceState
                        .Split(";")
                        .Select(s => s.Split(":"))
                        .ToDictionary(x => x[0], x => x[1]);
                    Assert.AreEqual(sampleString, traceStateMap["s"]);
                    Assert.AreEqual("rum", traceStateMap["o"]);
                    Assert.AreEqual(headerParts[2], traceStateMap["p"]);
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(tracingHeaderType), tracingHeaderType, null);
            }

            Assert.NotNull(traceInt);
            Assert.LessOrEqual(traceInt.GetByteCount(), tracingHeaderType == TracingHeaderType.Datadog ? 64 : 128);

            Assert.NotNull(spanInt);
            Assert.LessOrEqual(spanInt.GetByteCount(), 63);
        }
    }

    public class DatadogFirstPartyHostsTest
    {
        private ResourceTrackingHelper _trackingHelper;

        [SetUp]
        public void SetUp()
        {
            var options = new DatadogConfigurationOptions()
            {
                TraceSampleRate = 100.0f,
                FirstPartyHosts = new()
                {
                    new FirstPartyHostOption("example.com", TracingHeaderType.Datadog),
                    new FirstPartyHostOption("datadoghq.com", TracingHeaderType.B3),
                }
            };

            _trackingHelper = new ResourceTrackingHelper(options);
        }

        [Test]
        public void HeaderTypesForHostWithNoMatchReturnsNone()
        {
            // Given
            var uri = new Uri("https://nonfirstparty.com/request");

            // When
            var tracingHeaders = _trackingHelper.HeaderTypesForHost(uri);

            // Then
            Assert.AreEqual(TracingHeaderType.None, tracingHeaders);
        }

        [Test]
        public void HeaderTypesForHostMatchingReturnsTracingTypes()
        {
            // Given
            var uri = new Uri("https://example.com/request");

            // When
            var tracingHeaders = _trackingHelper.HeaderTypesForHost(uri);

            // Then
            Assert.AreEqual(TracingHeaderType.Datadog, tracingHeaders);
        }

        [Test]
        public void IsFirstPartyHostMatchesSubDomainReturnsTracingTypes()
        {
            // Given
            var uri = new Uri("https://app.datadoghq.com/request");

            // When
            var tracingHeaders = _trackingHelper.HeaderTypesForHost(uri);

            // Then
            Assert.AreEqual(TracingHeaderType.B3, tracingHeaders);
        }
    }
}
