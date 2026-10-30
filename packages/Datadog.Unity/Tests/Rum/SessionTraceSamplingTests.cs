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

            var first = helper.GenerateTraceContext(new TracingUuid(0, ulong.MaxValue), new TracingUuid(0, 1));

            Assert.IsTrue(first.sampled);
            Assert.AreEqual(0.1f, helper.GenerateDatadogAttributes(first)[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
            _ = provider.Received(1).Current;
            var second = helper.GenerateTraceContext(new TracingUuid(99, 1), new TracingUuid(0, 2));
            Assert.AreEqual(first.sampled, second.sampled);
        }

        [TestCase(0UL)]
        [TestCase(123UL)]
        [TestCase(ulong.MaxValue)]
        public void FallbackOnlyUsesTraceLowBitsAndTraceRate(ulong high)
        {
            var helper = new ResourceTrackingHelper(new() { SessionSampleRate = 0, TraceSampleRate = 10 });
            var context = helper.GenerateTraceContext(new TracingUuid(high, 1), new TracingUuid(0, 2));
            Assert.IsTrue(context.sampled);
            Assert.AreEqual(0.1f, helper.GenerateDatadogAttributes(context)[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
        }

        [Test]
        public void TraceFallbackPreservesTopBitOfLowHalf()
        {
            var helper = new ResourceTrackingHelper(new() { TraceSampleRate = 50 });
            Assert.IsFalse(helper.GenerateTraceContext(new TracingUuid(0, 1UL << 63), new TracingUuid(0, 1)).sampled);
        }

        [Test]
        public void ContextMetadataSurvivesSessionAndOptionsChanges()
        {
            using var provider = new RumContextStore();
            provider.Publish(RumContextStoreTests.FirstId);
            var options = new DatadogConfigurationOptions() { SessionSampleRate = 50, TraceSampleRate = 20 };
            var helper = new ResourceTrackingHelper(options, provider);
            var context = helper.GenerateTraceContext(new TracingUuid(0, 3), new TracingUuid(0, 4));
            provider.Publish(RumContextStoreTests.SecondId);
            options.TraceSampleRate = 100;
            options.SessionSampleRate = 100;

            var attributes = helper.GenerateDatadogAttributes(context);
            var headers = helper.GenerateTracingHeaders(context, TracingHeaderType.Datadog);
            Assert.IsTrue(context.sampled);
            Assert.AreEqual(0.1f, attributes[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
            Assert.IsInstanceOf<float>(attributes[ResourceTrackingHelper.DatadogAttributeKeys.RulePsr]);
            Assert.AreEqual("1", headers["x-datadog-sampling-priority"]);
            Assert.IsFalse(helper.GenerateTraceContext(new TracingUuid(0, 5), new TracingUuid(0, 6)).sampled);
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
            var trace = new TracingUuid(123, 456);
            var span = new TracingUuid(0, 789);
            var context = helper.GenerateTraceContext(trace, span);
            Assert.AreEqual(sampled, context.sampled);
            CollectionAssert.AreEquivalent(
                helper.GenerateTracingHeaders(new TraceContext(trace, span, null, sampled, 0.1f), type),
                helper.GenerateTracingHeaders(context, type));
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
