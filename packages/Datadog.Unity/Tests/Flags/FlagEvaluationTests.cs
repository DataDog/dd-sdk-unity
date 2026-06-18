// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class FlagEvaluationTests
    {
        private FlagsRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _repository = new FlagsRepository();
        }

        // Helper: build a minimal FlagsClient backed by an in-memory repository.
        // trackExposures and trackEvaluations are false so null trackers are safe.
        private FlagsClient MakeClient(FlagsRepository repo)
        {
            return new FlagsClient(
                repository: repo,
                exposureTracker: null,
                evaluationAggregator: null,
                fetcher: null,
                logger: null,
                trackExposures: false,
                trackEvaluations: false,
                onExposure: null,
                initialState: FlagsClientState.Ready);
        }

        [Test]
        public void BooleanFlagReturnsCorrectValue()
        {
            var flags = new Dictionary<string, FlagAssignment>
            {
                ["show-feature"] = new FlagAssignment("boolean", true, true, "alloc-1", "treatment", "TARGETING_MATCH"),
            };
            var context = new FlagsEvaluationContext("user-1");
            _repository.SetFlagsAndContext(context, flags);

            var assignment = _repository.GetFlagAssignment("show-feature");
            Assert.IsNotNull(assignment);
            Assert.IsTrue(assignment.TryGetValue<bool>(out var value));
            Assert.IsTrue(value);
        }

        [Test]
        public void StringFlagReturnsCorrectValue()
        {
            var flags = new Dictionary<string, FlagAssignment>
            {
                ["theme"] = new FlagAssignment("string", "dark", true, "alloc-1", "dark-mode", "TARGETING_MATCH"),
            };
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), flags);

            var assignment = _repository.GetFlagAssignment("theme");
            Assert.IsNotNull(assignment);
            Assert.IsTrue(assignment.TryGetValue<string>(out var value));
            Assert.AreEqual("dark", value);
        }

        [Test]
        public void IntegerFlagReturnsCorrectValue()
        {
            var flags = new Dictionary<string, FlagAssignment>
            {
                ["max-items"] = new FlagAssignment("integer", 42, true, "alloc-1", "high", "TARGETING_MATCH"),
            };
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), flags);

            var assignment = _repository.GetFlagAssignment("max-items");
            Assert.IsTrue(assignment.TryGetValue<int>(out var value));
            Assert.AreEqual(42, value);
        }

        [Test]
        public void DoubleFlagReturnsCorrectValue()
        {
            var flags = new Dictionary<string, FlagAssignment>
            {
                ["price"] = new FlagAssignment("number", 9.99, true, "alloc-1", "discount", "TARGETING_MATCH"),
            };
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), flags);

            var assignment = _repository.GetFlagAssignment("price");
            Assert.IsTrue(assignment.TryGetValue<double>(out var value));
            Assert.AreEqual(9.99, value, 0.001);
        }

        [Test]
        public void MissingFlagReturnsNull()
        {
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), new Dictionary<string, FlagAssignment>());

            var assignment = _repository.GetFlagAssignment("nonexistent");
            Assert.IsNull(assignment);
        }

        [Test]
        public void TypeMismatchReturnsFalse()
        {
            var flags = new Dictionary<string, FlagAssignment>
            {
                ["name"] = new FlagAssignment("string", "hello", true, "alloc-1", "greeting", "TARGETING_MATCH"),
            };
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), flags);

            var assignment = _repository.GetFlagAssignment("name");
            Assert.IsFalse(assignment.TryGetValue<bool>(out _));
        }

        [Test]
        public void RepositoryContextIsSet()
        {
            var context = new FlagsEvaluationContext("user-42", new Dictionary<string, object>
            {
                { "email", "test@example.com" },
            });
            _repository.SetFlagsAndContext(context, new Dictionary<string, FlagAssignment>());

            Assert.IsNotNull(_repository.Context);
            Assert.AreEqual("user-42", _repository.Context.TargetingKey);
            Assert.AreEqual("test@example.com", _repository.Context.Attributes["email"]);
        }

        [Test]
        public void SetFlagsReplacesExistingFlags()
        {
            var flags1 = new Dictionary<string, FlagAssignment>
            {
                ["flag-a"] = new FlagAssignment("boolean", true, true, "a", "v1", "DEFAULT"),
            };
            var flags2 = new Dictionary<string, FlagAssignment>
            {
                ["flag-b"] = new FlagAssignment("string", "value", true, "b", "v2", "TARGETING_MATCH"),
            };

            _repository.SetFlagsAndContext(new FlagsEvaluationContext("u1"), flags1);
            Assert.IsNotNull(_repository.GetFlagAssignment("flag-a"));
            Assert.IsNull(_repository.GetFlagAssignment("flag-b"));

            _repository.SetFlagsAndContext(new FlagsEvaluationContext("u1"), flags2);
            Assert.IsNull(_repository.GetFlagAssignment("flag-a"));
            Assert.IsNotNull(_repository.GetFlagAssignment("flag-b"));
        }

        // ─── GetDetails: AllocationKey threading ─────────────────────────────────────

        [Test]
        public void GetDetails_ReturnsAllocationKey()
        {
            var flags = new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("boolean", true, false, "alloc-xyz", "treatment", "TARGETING_MATCH"),
            };
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), flags);

            var client = MakeClient(_repository);
            var details = client.GetDetails("my-flag", false);

            Assert.AreEqual("alloc-xyz", details.AllocationKey);
        }

        [Test]
        public void GetDetails_MetadataContainsAllocationKey()
        {
            var flags = new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("boolean", true, false, "alloc-abc", "treatment", "TARGETING_MATCH"),
            };
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), flags);

            var client = MakeClient(_repository);
            var details = client.GetDetails("my-flag", false);

            Assert.IsTrue(details.Metadata.ContainsKey("allocationKey"),
                "Metadata must contain 'allocationKey'");
            Assert.AreEqual("alloc-abc", details.Metadata["allocationKey"]);
        }

        [Test]
        public void GetDetails_EmptyAllocationKey_MetadataOmitsAllocationKeyEntry()
        {
            var flags = new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("boolean", true, false, "", "treatment", "DEFAULT"),
            };
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), flags);

            var client = MakeClient(_repository);
            var details = client.GetDetails("my-flag", false);

            Assert.IsFalse(details.Metadata.ContainsKey("allocationKey"),
                "allocationKey must not appear in Metadata when the assignment has no allocation key");
        }
    }
}
