// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class EvaluationAggregatorTests
    {
        private List<FlagEvaluationEvent> _flushedEvents;
        private EvaluationAggregator _aggregator;

        [SetUp]
        public void SetUp()
        {
            _flushedEvents = new List<FlagEvaluationEvent>();
            _aggregator = new EvaluationAggregator(
                onFlush: events => _flushedEvents.AddRange(events),
                flushIntervalSeconds: 60.0f, // Large interval so only manual flush triggers
                maxAggregations: 1000);
        }

        [TearDown]
        public void TearDown()
        {
            _aggregator?.Dispose();
        }

        [Test]
        public void SingleEvaluationProducesOneEvent()
        {
            var assignment = new FlagAssignment("boolean", true, true, "alloc-1", "variant-a", "TARGETING_MATCH");
            var context = new FlagsEvaluationContext("user-123");

            _aggregator.RecordEvaluation("my-flag", assignment, context, null);
            _aggregator.Flush();

            Assert.AreEqual(1, _flushedEvents.Count);
            Assert.AreEqual("my-flag", _flushedEvents[0].FlagKey);
            Assert.AreEqual("variant-a", _flushedEvents[0].VariantKey);
            Assert.AreEqual("alloc-1", _flushedEvents[0].AllocationKey);
            Assert.AreEqual(1, _flushedEvents[0].EvaluationCount);
            Assert.IsNull(_flushedEvents[0].RuntimeDefaultUsed);
        }

        [Test]
        public void SameDimensionsAggregateIntoOneEvent()
        {
            var assignment = new FlagAssignment("boolean", true, true, "alloc-1", "variant-a", "TARGETING_MATCH");
            var context = new FlagsEvaluationContext("user-123");

            _aggregator.RecordEvaluation("my-flag", assignment, context, null);
            _aggregator.RecordEvaluation("my-flag", assignment, context, null);
            _aggregator.RecordEvaluation("my-flag", assignment, context, null);
            _aggregator.Flush();

            Assert.AreEqual(1, _flushedEvents.Count);
            Assert.AreEqual(3, _flushedEvents[0].EvaluationCount);
        }

        [Test]
        public void DifferentFlagsProduceSeparateEvents()
        {
            var assignment = new FlagAssignment("boolean", true, true, "alloc-1", "variant-a", "TARGETING_MATCH");
            var context = new FlagsEvaluationContext("user-123");

            _aggregator.RecordEvaluation("flag-1", assignment, context, null);
            _aggregator.RecordEvaluation("flag-2", assignment, context, null);
            _aggregator.Flush();

            Assert.AreEqual(2, _flushedEvents.Count);
        }

        [Test]
        public void DefaultReasonSetsRuntimeDefaultUsed()
        {
            var assignment = new FlagAssignment("boolean", true, true, "alloc-1", "variant-a", "DEFAULT");
            var context = new FlagsEvaluationContext("user-123");

            _aggregator.RecordEvaluation("my-flag", assignment, context, null);
            _aggregator.Flush();

            Assert.AreEqual(1, _flushedEvents.Count);
            Assert.AreEqual(true, _flushedEvents[0].RuntimeDefaultUsed);
            Assert.IsNull(_flushedEvents[0].VariantKey); // Null when runtime default
            Assert.IsNull(_flushedEvents[0].AllocationKey);
        }

        [Test]
        public void ErrorSetsRuntimeDefaultUsedAndErrorMessage()
        {
            var assignment = new FlagAssignment("boolean", true, true, "alloc-1", "variant-a", "ERROR");
            var context = new FlagsEvaluationContext("user-123");

            _aggregator.RecordEvaluation("my-flag", assignment, context, "FLAG_NOT_FOUND");
            _aggregator.Flush();

            Assert.AreEqual(1, _flushedEvents.Count);
            Assert.AreEqual(true, _flushedEvents[0].RuntimeDefaultUsed);
            Assert.AreEqual("FLAG_NOT_FOUND", _flushedEvents[0].ErrorMessage);
        }

        [Test]
        public void FlushClearsAggregations()
        {
            var assignment = new FlagAssignment("boolean", true, true, "alloc-1", "variant-a", "TARGETING_MATCH");
            var context = new FlagsEvaluationContext("user-123");

            _aggregator.RecordEvaluation("my-flag", assignment, context, null);
            _aggregator.Flush();

            Assert.AreEqual(1, _flushedEvents.Count);
            _flushedEvents.Clear();

            // Second flush should produce no events
            _aggregator.Flush();
            Assert.AreEqual(0, _flushedEvents.Count);
        }

        [Test]
        public void MaxAggregationsTriggersFlush()
        {
            var smallAggregator = new EvaluationAggregator(
                onFlush: events => _flushedEvents.AddRange(events),
                flushIntervalSeconds: 60.0f,
                maxAggregations: 3);

            var context = new FlagsEvaluationContext("user-123");

            // Each different flag key creates a new aggregation
            smallAggregator.RecordEvaluation("flag-1",
                new FlagAssignment("boolean", true, true, "a", "v", "TARGETING_MATCH"), context, null);
            smallAggregator.RecordEvaluation("flag-2",
                new FlagAssignment("boolean", true, true, "a", "v", "TARGETING_MATCH"), context, null);

            Assert.AreEqual(0, _flushedEvents.Count); // Not yet at max

            smallAggregator.RecordEvaluation("flag-3",
                new FlagAssignment("boolean", true, true, "a", "v", "TARGETING_MATCH"), context, null);

            // Should have auto-flushed at 3 aggregations
            Assert.AreEqual(3, _flushedEvents.Count);

            smallAggregator.Dispose();
        }
    }
}
