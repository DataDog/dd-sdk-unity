// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.Collections.Generic;
using NSubstitute;
using NUnit.Framework;

namespace Datadog.Unity.Rum.Tests
{
    public class SessionTraceSamplingTests
    {
        [Test]
        public void SessionSamplingUsesOneSnapshotAndCombinesRates()
        {
            var provider = Substitute.For<IRumContextProvider>();
            provider.Current.Returns(RumSessionContext.FromSessionId(RumContextStoreTests.FirstId));
            var helper = new ResourceTrackingHelper(new() { SessionSampleRate = 50, TraceSampleRate = 20 }, provider);

            var first = helper.GenerateTraceContext();

            Assert.IsTrue(first.sampled);
            Assert.AreEqual(0.1f, helper.GenerateDatadogAttributes(first)[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
            _ = provider.Received(1).Current;
            var second = helper.GenerateTraceContext();
            Assert.AreEqual(first.sampled, second.sampled);
        }

        [TestCase(0.0f, false)]
        [TestCase(100.0f, true)]
        public void MissingSessionUsesTraceRate(float traceRate, bool expectedSampled)
        {
            var helper = new ResourceTrackingHelper(new() { SessionSampleRate = 0, TraceSampleRate = traceRate });
            var context = helper.GenerateTraceContext();
            Assert.AreEqual(expectedSampled, context.sampled);
            Assert.AreEqual(traceRate / 100.0f, helper.GenerateDatadogAttributes(context)[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
        }

        [Test]
        public void RequestsBeforeFirstCallbackKeepTheirTraceBasedDecision()
        {
            using var provider = new RumContextStore();
            var helper = new ResourceTrackingHelper(new() { SessionSampleRate = 10, TraceSampleRate = 100 }, provider);

            var beforeCallback = helper.GenerateTraceContext();
            Assert.IsTrue(beforeCallback.sampled);

            provider.Publish(RumContextStoreTests.SecondId);
            var afterCallback = helper.GenerateTraceContext();
            Assert.IsFalse(afterCallback.sampled);
            Assert.AreEqual(0.1f, helper.GenerateDatadogAttributes(afterCallback)[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
            Assert.AreEqual(1.0f, helper.GenerateDatadogAttributes(beforeCallback)[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
            Assert.AreEqual("1", helper.GenerateTracingHeaders(beforeCallback, TracingHeaderType.Datadog)["x-datadog-sampling-priority"]);
        }

        [Test]
        public void ContextMetadataSurvivesSessionAndOptionsChanges()
        {
            using var provider = new RumContextStore();
            provider.Publish(RumContextStoreTests.FirstId);
            var options = new DatadogConfigurationOptions() { SessionSampleRate = 50, TraceSampleRate = 20 };
            var helper = new ResourceTrackingHelper(options, provider);
            var context = helper.GenerateTraceContext();
            provider.Publish(RumContextStoreTests.SecondId);
            options.TraceSampleRate = 100;
            options.SessionSampleRate = 100;

            var attributes = helper.GenerateDatadogAttributes(context);
            var headers = helper.GenerateTracingHeaders(context, TracingHeaderType.Datadog);
            Assert.IsTrue(context.sampled);
            Assert.AreEqual(0.1f, attributes[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
            Assert.IsInstanceOf<float>(attributes[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
            Assert.AreEqual("1", headers["x-datadog-sampling-priority"]);
            Assert.IsFalse(helper.GenerateTraceContext().sampled);
        }

        [Test]
        public void ExcludedSessionDoesNotFallBackToTraceId()
        {
            using var provider = new RumContextStore();
            provider.Publish(RumContextStoreTests.FirstId);
            var helper = new ResourceTrackingHelper(new() { SessionSampleRate = 0, TraceSampleRate = 100 }, provider);
            Assert.IsFalse(helper.GenerateTraceContext().sampled);
            provider.Publish(null);
            Assert.IsTrue(helper.GenerateTraceContext().sampled);
        }

        [Test, Combinatorial]
        public void SessionDecisionsDriveAllHeaderFormats(
            [Values(TracingHeaderType.Datadog, TracingHeaderType.B3, TracingHeaderType.B3Multi, TracingHeaderType.TraceContext)] TracingHeaderType type,
            [Values(TraceContextInjection.All, TraceContextInjection.Sampled)] TraceContextInjection injection,
            [Values(true, false)] bool sampled)
        {
            using var provider = new RumContextStore();
            provider.Publish(sampled ? RumContextStoreTests.FirstId : RumContextStoreTests.SecondId);
            var helper = new ResourceTrackingHelper(new()
            {
                SessionSampleRate = 50, TraceSampleRate = 20, TraceContextInjection = injection,
            }, provider);
            var context = helper.GenerateTraceContext();
            Assert.AreEqual(sampled, context.sampled);
            CollectionAssert.AreEquivalent(
                helper.GenerateTracingHeaders(new TraceContext(context.traceId, context.spanId, null, sampled), type),
                helper.GenerateTracingHeaders(context, type));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CallerProvidedContextPreservesIdentifiersAndSampling(bool sampled)
        {
            using var provider = new RumContextStore();
            provider.Publish(RumContextStoreTests.FirstId);
            var helper = new ResourceTrackingHelper(new()
            {
                SessionSampleRate = 0, TraceSampleRate = 25, TraceContextInjection = TraceContextInjection.All,
            }, provider);
            var traceId = new TracingUuid(123, 456);
            var spanId = new TracingUuid(0, 789);
            var parentId = new TracingUuid(0, 42);
            var context = new TraceContext(traceId, spanId, parentId, sampled);

            var headers = helper.GenerateTracingHeaders(context, TracingHeaderType.Datadog | TracingHeaderType.B3);
            Assert.AreEqual(traceId.ToString(TraceIdRepresentation.LowDec), headers["x-datadog-trace-id"]);
            Assert.AreEqual(spanId.ToString(TraceIdRepresentation.Dec), headers["x-datadog-parent-id"]);
            Assert.AreEqual(sampled ? "1" : "0", headers["x-datadog-sampling-priority"]);
            var expectedB3 = sampled
                ? $"{traceId.ToString(TraceIdRepresentation.Hex32Chars)}-{spanId.ToString(TraceIdRepresentation.Hex16Chars)}-1-" +
                  parentId.ToString(TraceIdRepresentation.Hex16Chars)
                : "0";
            Assert.AreEqual(expectedB3, headers["b3"]);

            var attributes = helper.GenerateDatadogAttributes(context);
            Assert.AreEqual(0.25f, attributes[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
            if (sampled)
            {
                Assert.AreEqual(traceId.ToString(TraceIdRepresentation.Hex32Chars), attributes[ResourceTrackingHelper.DatadogAttributeKeys.TraceId]);
                Assert.AreEqual(spanId.ToString(TraceIdRepresentation.Dec), attributes[ResourceTrackingHelper.DatadogAttributeKeys.SpanId]);
            }
            else
            {
                Assert.IsFalse(attributes.ContainsKey(ResourceTrackingHelper.DatadogAttributeKeys.TraceId));
                Assert.IsFalse(attributes.ContainsKey(ResourceTrackingHelper.DatadogAttributeKeys.SpanId));
            }
        }

        [TestCase(0.0f)]
        [TestCase(0.3f)]
        [TestCase(1.0f)]
        public void ResourceMetadataUsesCapturedRate(float rulePsr)
        {
            var helper = new ResourceTrackingHelper(new() { SessionSampleRate = 50, TraceSampleRate = 20 });
            var context = new TraceContext(new TracingUuid(0, 1), new TracingUuid(0, 2), null, rulePsr > 0, rulePsr);
            Assert.AreEqual(rulePsr, helper.GenerateDatadogAttributes(context)[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
        }
    }
}
