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

        [Test]
        public void DifferentContextAttributesProduceSeparateEvents()
        {
            var assignment = new FlagAssignment("boolean", true, true, "alloc-1", "variant-a", "TARGETING_MATCH");
            var contextA = new FlagsEvaluationContext("user-123", new Dictionary<string, object> { ["plan"] = "free" });
            var contextB = new FlagsEvaluationContext("user-123", new Dictionary<string, object> { ["plan"] = "paid" });

            _aggregator.RecordEvaluation("my-flag", assignment, contextA, null);
            _aggregator.RecordEvaluation("my-flag", assignment, contextB, null);
            _aggregator.Flush();

            Assert.AreEqual(2, _flushedEvents.Count);
        }

        [Test]
        public void SameContextAttributesDifferentInsertionOrderAggregates()
        {
            var assignment = new FlagAssignment("boolean", true, true, "alloc-1", "variant-a", "TARGETING_MATCH");

            // Same logical context built in different insertion order
            var contextA = new FlagsEvaluationContext("user-123", new Dictionary<string, object>
            {
                ["alpha"] = "1",
                ["beta"] = "2",
            });
            var contextB = new FlagsEvaluationContext("user-123", new Dictionary<string, object>
            {
                ["beta"] = "2",
                ["alpha"] = "1",
            });

            _aggregator.RecordEvaluation("my-flag", assignment, contextA, null);
            _aggregator.RecordEvaluation("my-flag", assignment, contextB, null);
            _aggregator.Flush();

            Assert.AreEqual(1, _flushedEvents.Count);
            Assert.AreEqual(2, _flushedEvents[0].EvaluationCount);
        }

        public class AggregationKeyTests
        {
            private static EvaluationAggregator.AggregationKey MakeKey(
                string flagKey = "flag",
                string variantKey = "variant",
                string allocationKey = "alloc",
                string targetingKey = "user",
                string errorMessage = null,
                Dictionary<string, object> context = null)
            {
                return new EvaluationAggregator.AggregationKey(
                    flagKey, variantKey, allocationKey, targetingKey, errorMessage, context);
            }

            [Test]
            public void NullAndEmptyContextAreEqual()
            {
                var withNull = MakeKey(context: null);
                var withEmpty = MakeKey(context: new Dictionary<string, object>());

                Assert.AreEqual(withNull, withEmpty);
                Assert.AreEqual(withNull.GetHashCode(), withEmpty.GetHashCode());
            }

            [Test]
            public void SameAttributesDifferentInsertionOrderAreEqual()
            {
                var keyA = MakeKey(context: new Dictionary<string, object> { ["x"] = 1, ["y"] = 2 });
                var keyB = MakeKey(context: new Dictionary<string, object> { ["y"] = 2, ["x"] = 1 });

                Assert.AreEqual(keyA, keyB);
                Assert.AreEqual(keyA.GetHashCode(), keyB.GetHashCode());
            }

            [Test]
            public void DifferentAttributeValuesAreNotEqual()
            {
                var keyA = MakeKey(context: new Dictionary<string, object> { ["x"] = "a" });
                var keyB = MakeKey(context: new Dictionary<string, object> { ["x"] = "b" });

                Assert.AreNotEqual(keyA, keyB);
            }

            [Test]
            public void DifferentAttributeKeysAreNotEqual()
            {
                var keyA = MakeKey(context: new Dictionary<string, object> { ["x"] = 1 });
                var keyB = MakeKey(context: new Dictionary<string, object> { ["y"] = 1 });

                Assert.AreNotEqual(keyA, keyB);
            }

            [Test]
            public void DifferentFlagFieldsAreNotEqual()
            {
                var keyA = MakeKey(flagKey: "flag-a");
                var keyB = MakeKey(flagKey: "flag-b");

                Assert.AreNotEqual(keyA, keyB);
                Assert.AreNotEqual(keyA.GetHashCode(), keyB.GetHashCode());
            }
        }
    }
}
