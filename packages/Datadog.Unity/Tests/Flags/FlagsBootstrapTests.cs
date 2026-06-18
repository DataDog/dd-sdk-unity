// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Datadog.Unity.Logs;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    /// <summary>
    /// Tests for FlagsClient bootstrap-from-cache behavior (LIFE-01, LIFE-02).
    /// </summary>
    public class FlagsBootstrapTests
    {
        // Minimal valid server response payload that ParseResponse accepts without returning null.
        // Structure: {"data":{"attributes":{"flags":{"flag-key":{...}}}}}
        private const string MinimalValidPayloadEmpty =
            "{\"data\":{\"attributes\":{\"flags\":{}}}}";

        private const string ValidPayloadWithBoolFlag =
            "{\"data\":{\"attributes\":{\"flags\":{" +
            "\"show-feature\":{\"variationType\":\"boolean\",\"variationValue\":true,\"doLog\":true,\"allocationKey\":\"alloc-1\",\"variationKey\":\"treatment\",\"reason\":\"TARGETING_MATCH\"}" +
            "}}}}";

        // ── Test 1: cache hit with valid data → State == Ready and value returned ──

        [Test]
        public void Bootstrap_ValidCacheHit_StateIsReady()
        {
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = MinimalValidPayloadEmpty,
            });

            var client = MakeClient(cacheReader: reader);

            Assert.AreEqual(FlagsClientState.Ready, client.State,
                "State must be Ready after bootstrap with valid cache data");
        }

        [Test]
        public void Bootstrap_ValidCacheHit_GetBooleanValueReturnsCachedValue()
        {
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = ValidPayloadWithBoolFlag,
            });

            var client = MakeClient(cacheReader: reader);

            Assert.AreEqual(FlagsClientState.Ready, client.State,
                "State must be Ready after bootstrap with valid flag payload");
            Assert.IsTrue(client.GetBooleanValue("show-feature", false),
                "GetBooleanValue must return cached flag value after bootstrap");
        }

        // ── Test 2: cache miss → State == NotReady ─────────────────────────

        [Test]
        public void Bootstrap_CacheMiss_StateIsNotReady()
        {
            var reader = new FakeReader(null);

            var client = MakeClient(cacheReader: reader);

            Assert.AreEqual(FlagsClientState.NotReady, client.State,
                "State must remain NotReady when cache returns null");
        }

        // ── Test 3: corrupt payload → State == NotReady ────────────────────

        [Test]
        public void Bootstrap_CorruptPayload_StateIsNotReady()
        {
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = "not-valid-json{{{{",
            });

            var client = MakeClient(cacheReader: reader);

            Assert.AreEqual(FlagsClientState.NotReady, client.State,
                "State must remain NotReady when cached payload is corrupt");
        }

        // ── Test 4: null cacheReader → State == NotReady (existing behavior) ──

        [Test]
        public void Bootstrap_NullCacheReader_StateIsNotReady()
        {
            var client = MakeClient(cacheReader: null);

            Assert.AreEqual(FlagsClientState.NotReady, client.State,
                "State must remain NotReady when no cacheReader is provided");
        }

        // ── Test 5: empty/null payload → State == NotReady ────────────────

        [Test]
        public void Bootstrap_EmptyPayload_StateIsNotReady()
        {
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = null,
            });

            var client = MakeClient(cacheReader: reader);

            Assert.AreEqual(FlagsClientState.NotReady, client.State,
                "State must remain NotReady when cached envelope has null Payload");
        }

        [Test]
        public void Bootstrap_EmptyStringPayload_StateIsNotReady()
        {
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = string.Empty,
            });

            var client = MakeClient(cacheReader: reader);

            Assert.AreEqual(FlagsClientState.NotReady, client.State,
                "State must remain NotReady when cached envelope has empty Payload");
        }

        // ── Test 6: LIFE-02 round-trip — CachedAt survives write→read ──────

        [Test]
        public void Bootstrap_CachedAt_SurvivesWriteReadRoundTrip()
        {
            var store = new DictionaryKeyValueStore();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "abcdefghij", null);
            var context = new FlagsEvaluationContext("user-1");

            cacheStore.Write(MinimalValidPayloadEmpty, context);

            var envelope = ((IFlagsCacheReader)cacheStore).Read(null);

            Assert.IsNotNull(envelope, "Read must return an envelope after Write");
            Assert.IsFalse(string.IsNullOrEmpty(envelope.CachedAt),
                "CachedAt must be non-null after write→read round-trip (LIFE-02)");
        }

        // ── Context restoration after bootstrap ───────────────────────────

        [Test]
        public void Bootstrap_EnvelopeWithContext_RepositoryContextIsRestored()
        {
            var repo = new FlagsRepository();
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = MinimalValidPayloadEmpty,
                Context = new FlagsCacheEnvelopeDto.FlagsEvaluationContextDto
                {
                    TargetingKey = "user-5",
                },
            });

            MakeClient(cacheReader: reader, repository: repo);

            Assert.IsNotNull(repo.Context, "Context must be non-null after bootstrap with envelope context");
            Assert.AreEqual("user-5", repo.Context.TargetingKey,
                "TargetingKey must be restored from envelope context");
        }

        [Test]
        public void Bootstrap_EnvelopeWithContext_EmptyTargetingKey_RepositoryContextIsNull()
        {
            var repo = new FlagsRepository();
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = MinimalValidPayloadEmpty,
                Context = new FlagsCacheEnvelopeDto.FlagsEvaluationContextDto
                {
                    TargetingKey = string.Empty,
                },
            });

            MakeClient(cacheReader: reader, repository: repo);

            Assert.IsNull(repo.Context, "Context must be null when envelope has empty TargetingKey");
        }

        [Test]
        public void Bootstrap_EnvelopeWithNullContext_RepositoryContextIsNull()
        {
            var repo = new FlagsRepository();
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = MinimalValidPayloadEmpty,
                Context = null,
            });

            MakeClient(cacheReader: reader, repository: repo);

            Assert.IsNull(repo.Context, "Context must be null when envelope.Context is null");
        }

        [Test]
        public void Bootstrap_EnvelopeWithContextAndAttributes_AttributesSurvive()
        {
            var repo = new FlagsRepository();
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = MinimalValidPayloadEmpty,
                Context = new FlagsCacheEnvelopeDto.FlagsEvaluationContextDto
                {
                    TargetingKey = "user-5",
                    Attributes = new System.Collections.Generic.Dictionary<string, string> { { "plan", "pro" } },
                },
            });

            MakeClient(cacheReader: reader, repository: repo);

            Assert.IsNotNull(repo.Context, "Context must be non-null after bootstrap");
            Assert.AreEqual("user-5", repo.Context.TargetingKey, "TargetingKey must survive bootstrap");
            Assert.AreEqual("pro", repo.Context.Attributes["plan"],
                "Attributes must survive bootstrap from envelope context");
        }

        // ── LIFE-05: bootstrap → network fetch → cache updated + repo refreshed ──

        /// <summary>
        /// Verifies the LIFE-05 contract at the storage layer: after a successful fetch
        /// the cache writer receives the new payload and the repository reflects updated flags.
        ///
        /// Note: <c>SetEvaluationContext</c> is the LIFE-05 trigger in production but requires a
        /// real <c>PrecomputeAssignmentsFetcher</c> (no interface abstraction exists yet). This test
        /// covers the downstream contract — that the components wired together by
        /// <c>SetEvaluationContext</c> (cache write + repository update) behave correctly — using
        /// a <c>CapturingWriter</c> and direct repository manipulation to simulate a successful
        /// fetch response.
        /// </summary>
        [Test]
        public void Life05_BootstrapThenFetchSucceeds_CacheAndRepositoryUpdated()
        {
            // Arrange: bootstrap from cache with old flags (empty).
            var capturedWrites = new List<string>();
            var writer = new CapturingWriter(capturedWrites);
            var reader = new FakeReader(new FlagsCacheEnvelopeDto
            {
                CachedAt = "2025-01-01T00:00:00Z",
                Payload = MinimalValidPayloadEmpty,
                Context = new FlagsCacheEnvelopeDto.FlagsEvaluationContextDto { TargetingKey = "user-old" },
            });
            var repo = new FlagsRepository();
            var client = MakeClient(cacheReader: reader, cacheWriter: writer, repository: repo);

            Assert.AreEqual(FlagsClientState.Ready, client.State,
                "Bootstrap must set state to Ready (pre-condition for LIFE-05)");
            Assert.IsFalse(client.GetBooleanValue("show-feature", false),
                "Flag must not exist before the fetch updates the repository");

            // Act: simulate a successful network fetch — the same steps SetEvaluationContext
            // executes inside its Fetch callback when flags != null.
            var newContext = new FlagsEvaluationContext("user-new");
            var newFlags = PrecomputeAssignmentsFetcher.ParseResponse(ValidPayloadWithBoolFlag);
            writer.Write(ValidPayloadWithBoolFlag, newContext);   // mirrors _cacheWriter?.Write(rawJson, context)
            repo.SetFlagsAndContext(newContext, newFlags);         // mirrors _repository.SetFlagsAndContext(context, flags)

            // Assert: cache received the new write and repository serves the updated flag.
            Assert.AreEqual(1, capturedWrites.Count,
                "Cache writer must receive exactly one write after fetch (LIFE-05)");
            Assert.IsTrue(client.GetBooleanValue("show-feature", false),
                "Repository must serve updated flag value after fetch completes (LIFE-05)");
            Assert.AreEqual("user-new", repo.Context.TargetingKey,
                "Repository context must reflect the new evaluation context after fetch");
        }

        /// <summary>
        /// Verifies that after bootstrap from cache, the cache store (using the real
        /// <c>FlagsCacheStore</c>) is updated on a subsequent write — covering the full
        /// write→read→bootstrap→write round-trip at the storage layer.
        /// </summary>
        [Test]
        public void Life05_BootstrapThenWrite_CacheStoreReflectsNewData()
        {
            var store = new DictionaryKeyValueStore();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "abcdefghij", null);
            var initialContext = new FlagsEvaluationContext("user-old");

            // Write initial (empty) flags into the cache.
            cacheStore.Write(MinimalValidPayloadEmpty, initialContext);

            // Bootstrap: a new client reads from the cache.
            var client = MakeClient(cacheReader: cacheStore, repository: new FlagsRepository());
            Assert.AreEqual(FlagsClientState.Ready, client.State,
                "Client must be Ready after bootstrapping from real FlagsCacheStore");

            // Simulate a successful fetch: write updated flags into the same cache store.
            var updatedContext = new FlagsEvaluationContext("user-new");
            cacheStore.Write(ValidPayloadWithBoolFlag, updatedContext);

            // Verify the store now holds the updated envelope.
            var updatedEnvelope = ((IFlagsCacheReader)cacheStore).Read(null);
            Assert.IsNotNull(updatedEnvelope, "Store must return envelope after second write");
            Assert.AreEqual(ValidPayloadWithBoolFlag, updatedEnvelope.Payload,
                "Payload must reflect the updated flags from the fetch");
            Assert.AreEqual("user-new", updatedEnvelope.Context?.TargetingKey,
                "Context in envelope must reflect the new evaluation context");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static FlagsClient MakeClient(
            IFlagsCacheReader cacheReader = null,
            IFlagsCacheWriter cacheWriter = null,
            FlagsRepository repository = null)
        {
            return new FlagsClient(
                repository: repository ?? new FlagsRepository(),
                exposureTracker: null,
                evaluationAggregator: null,
                fetcher: null,
                logger: null,
                trackExposures: false,
                trackEvaluations: false,
                onExposure: null,
                cacheWriter: cacheWriter,
                cacheReader: cacheReader);
        }

        private class FakeReader : IFlagsCacheReader
        {
            private readonly FlagsCacheEnvelopeDto _envelope;

            public FakeReader(FlagsCacheEnvelopeDto envelope)
            {
                _envelope = envelope;
            }

            public FlagsCacheEnvelopeDto? Read(FlagsEvaluationContext context) => _envelope;
        }

        private class CapturingWriter : IFlagsCacheWriter
        {
            private readonly List<string> _captured;

            public CapturingWriter(List<string> captured)
            {
                _captured = captured;
            }

            public void Write(string rawJson, FlagsEvaluationContext context)
            {
                _captured.Add(rawJson);
            }
        }
    }
}
