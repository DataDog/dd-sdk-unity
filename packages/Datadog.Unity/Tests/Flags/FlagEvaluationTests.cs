// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
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
        public void PublicGenericReadsPreserveNumericCompatibility()
        {
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user"), new Dictionary<string, FlagAssignment>
            {
                ["long"] = new FlagAssignment("number", 5000000000L, false, "a", "v", "TARGETING_MATCH"),
                ["float"] = new FlagAssignment("number", 1.5, false, "a", "v", "TARGETING_MATCH"),
            });
            using var client = MakeClient(_repository);
            Assert.AreEqual(5000000000L, client.GetDetails<long>("long", 0).Value);
            Assert.IsNull(client.GetDetails<long>("long", 0).Error);
            Assert.AreEqual(1.5f, client.GetDetails<float>("float", 0).Value);
            Assert.IsNull(client.GetDetails<float>("float", 0).Error);
            Assert.AreEqual(5000000000L, client.GetObjectValue("long", null));
            Assert.AreEqual("ERROR", client.GetDetails<bool>("long", false).Reason);
            Assert.AreEqual("ERROR", client.GetDetails<bool>("missing", false).Reason);
        }

        [Test]
        public void SerialIdChangeSendsBothExposures()
        {
            var exposures = new List<ExposureEvent>();
            using var client = new FlagsClient(_repository, new ExposureTracker(), null, null, null,
                true, false, exposures.Add, FlagsClientState.Ready);
            var flags = new Dictionary<string, FlagAssignment>();
            var context = new FlagsEvaluationContext("user");
            foreach (var serial in new long[] { 1, 1, 2, 2 })
            {
                flags["flag"] = new FlagAssignment("boolean", true, true, "alloc", "variant", "TARGETING_MATCH", serial);
                _repository.SetFlagsAndContext(context, flags);
                Assert.IsTrue(client.GetBooleanValue("flag", false));
            }
            Assert.AreEqual(2, exposures.Count);
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
        public void GetDetails_NullAllocationKey_ReturnsNull()
        {
            var flags = new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("boolean", true, false, null, "treatment", "DEFAULT"),
            };
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), flags);

            var client = MakeClient(_repository);
            var details = client.GetDetails("my-flag", false);

            Assert.IsNull(details.AllocationKey,
                "AllocationKey must be null when the assignment has no allocation key");
        }

        // ─── State after a failed fetch ──────────────────────────────────────────────

        // Completes each fetch with the next queued result; null means the fetch failed.
        private sealed class QueuedFetcher : PrecomputeAssignmentsFetcher
        {
            private readonly Queue<Dictionary<string, FlagAssignment>> _results;

            public QueuedFetcher(params Dictionary<string, FlagAssignment>[] results)
                : base("https://example.invalid/precompute-assignments", "token", null, "prod", null)
            {
                _results = new Queue<Dictionary<string, FlagAssignment>>(results);
            }

            public override void Fetch(
                FlagsEvaluationContext context,
                Action<Dictionary<string, FlagAssignment>> onComplete)
            {
                onComplete(_results.Dequeue());
            }
        }

        private static FlagsClient MakeClient(PrecomputeAssignmentsFetcher fetcher) => new FlagsClient(
            repository: new FlagsRepository(),
            exposureTracker: null,
            evaluationAggregator: null,
            fetcher: fetcher,
            logger: null,
            trackExposures: false,
            trackEvaluations: false,
            onExposure: null);

        [Test]
        public void FailedFetchAfterAnEmptyConfigurationIsStale()
        {
            var client = MakeClient(new QueuedFetcher(new Dictionary<string, FlagAssignment>(), null));
            var context = new FlagsEvaluationContext("user-1");

            client.SetEvaluationContext(context);
            Assert.AreEqual(FlagsClientState.Ready, client.State);
            Assert.AreEqual(FlagEvaluationError.FlagNotFound, client.GetBooleanDetails("missing", false).Error);

            client.SetEvaluationContext(context);
            Assert.AreEqual(FlagsClientState.Stale, client.State);
        }

        [Test]
        public void FailedFetchWithoutAConfigurationIsError()
        {
            var client = MakeClient(new QueuedFetcher((Dictionary<string, FlagAssignment>)null));

            client.SetEvaluationContext(new FlagsEvaluationContext("user-1"));

            Assert.AreEqual(FlagsClientState.Error, client.State);
        }

        private const string OneFlagBody = @"{""data"": {""attributes"": {""flags"": {
            ""a"": {""variationType"": ""boolean"", ""variationValue"": true, ""doLog"": false,
                    ""allocationKey"": ""alloc"", ""variationKey"": ""on"", ""reason"": ""STATIC""}}}}}";

        private const string EmptyFlagsBody = @"{""data"": {""attributes"": {""flags"": {}}}}";

        // Completes each fetch by handling the next queued body as an HTTP 200 response.
        private sealed class SuccessBodyFetcher : PrecomputeAssignmentsFetcher
        {
            private readonly Queue<string> _bodies;

            public SuccessBodyFetcher(params string[] bodies)
                : base("https://example.invalid/precompute-assignments", "token", null, "prod", null)
            {
                _bodies = new Queue<string>(bodies);
            }

            public override void Fetch(
                FlagsEvaluationContext context,
                Action<Dictionary<string, FlagAssignment>> onComplete)
            {
                HandleResponse(new SuccessResponse(_bodies.Dequeue()), onComplete);
            }
        }

        private sealed class SuccessResponse : IPrecomputeResponse
        {
            public SuccessResponse(string body)
            {
                Body = body;
            }

            public bool Succeeded => true;

            public long StatusCode => 200;

            public string Body { get; }

            public string GetHeader(string name) => null;
        }

        [Test]
        [TestCase("not json")]
        [TestCase(@"{""data"": {}}")]
        public void SuccessWithoutAFlagsObjectIsAFailedFetch(string body)
        {
            var client = MakeClient(new SuccessBodyFetcher(body));

            client.SetEvaluationContext(new FlagsEvaluationContext("user-1"));

            Assert.AreEqual(FlagsClientState.Error, client.State);
        }

        [Test]
        [TestCase("not json")]
        [TestCase(@"{""data"": {}}")]
        public void SuccessWithoutAFlagsObjectKeepsTheLastConfiguration(string body)
        {
            var client = MakeClient(new SuccessBodyFetcher(OneFlagBody, body));
            var context = new FlagsEvaluationContext("user-1");

            client.SetEvaluationContext(context);
            Assert.AreEqual(FlagsClientState.Ready, client.State);

            client.SetEvaluationContext(context);
            Assert.AreEqual(FlagsClientState.Stale, client.State);
            Assert.IsTrue(client.GetBooleanDetails("a", false).Value);
        }

        [Test]
        public void SuccessWithAnEmptyFlagsObjectInstallsAnEmptyConfiguration()
        {
            var client = MakeClient(new SuccessBodyFetcher(EmptyFlagsBody, "not json"));
            var context = new FlagsEvaluationContext("user-1");

            client.SetEvaluationContext(context);
            Assert.AreEqual(FlagsClientState.Ready, client.State);
            Assert.AreEqual(FlagEvaluationError.FlagNotFound, client.GetBooleanDetails("a", false).Error);

            client.SetEvaluationContext(context);
            Assert.AreEqual(FlagsClientState.Stale, client.State);
        }

        [Test]
        public void RepositoryHasAConfigurationOnceOneIsInstalled()
        {
            Assert.IsFalse(_repository.HasConfiguration());

            _repository.SetFlagsAndContext(
                new FlagsEvaluationContext("user-1"), new Dictionary<string, FlagAssignment>());

            Assert.IsTrue(_repository.HasConfiguration());
        }
    }
}
