// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Text;
using Datadog.Unity.Logs;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class FlagsCacheStoreTests
    {
        // ── Key computation ────────────────────────────────────────────────

        [Test]
        public void ComputeKey_LongToken_TakesLastEightChars()
        {
            var key = FlagsCacheStore.ComputeKey("us1", "prod", "abcdefghij");
            Assert.AreEqual("dd_flags_v1_us1_prod_cdefghij", key);
        }

        [Test]
        public void ComputeKey_ShortToken_UsedVerbatim()
        {
            var key = FlagsCacheStore.ComputeKey("us1", "prod", "short");
            Assert.AreEqual("dd_flags_v1_us1_prod_short", key);
        }

        [Test]
        public void ComputeKey_NullToken_TreatedAsEmpty()
        {
            var key = FlagsCacheStore.ComputeKey("us1", "prod", null);
            Assert.AreEqual("dd_flags_v1_us1_prod_", key);
        }

        [Test]
        public void ComputeKey_ExactlyEightChars_UsedVerbatim()
        {
            var key = FlagsCacheStore.ComputeKey("us1", "prod", "12345678");
            Assert.AreEqual("dd_flags_v1_us1_prod_12345678", key);
        }

        // ── Write: small payload (< 400 KB) ────────────────────────────────

        [Test]
        public void Write_SmallPayload_CallsSetStringAndSave_NoLogEmitted()
        {
            var store = new FakeKeyValueStore();
            var logSink = new FakeLogger();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "abcdefghij", logSink);
            var context = new FlagsEvaluationContext("user-1");

            cacheStore.Write("{\"flags\":{}}", context);

            Assert.AreEqual(1, store.SetStringCallCount, "SetString must be called once");
            Assert.AreEqual(1, store.SaveCallCount, "Save must be called once");
            Assert.IsFalse(logSink.WarnEmitted, "No warn log for small payloads");
        }

        // ── Write: warn threshold (>= 400 KB but < 500 KB) ─────────────────

        [Test]
        public void Write_401KbPayload_CallsSetStringAndSave_EmitsWarnLog()
        {
            var store = new FakeKeyValueStore();
            var logSink = new FakeLogger();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "abcdefghij", logSink);
            var context = new FlagsEvaluationContext("user-1");

            // Build a rawJson large enough that the serialized envelope will be >= 400 KB.
            // The envelope adds overhead ("cachedAt" key + ISO timestamp ~40 chars, "payload" key).
            // Use 401 * 1024 = 410,624 bytes of ASCII to ensure the threshold is crossed.
            var bigJson = "{\"data\":\"" + new string('x', 401 * 1024) + "\"}";

            cacheStore.Write(bigJson, context);

            Assert.AreEqual(1, store.SetStringCallCount, "SetString must be called");
            Assert.AreEqual(1, store.SaveCallCount, "Save must be called");
            Assert.IsTrue(logSink.WarnEmitted, "Warn log must be emitted at 401 KB");
            Assert.IsTrue(logSink.LastWarnMessage.Contains("large"), "Warn message must contain 'large'");
        }

        // ── Write: skip threshold (>= 500 KB) ──────────────────────────────

        [Test]
        public void Write_501KbPayload_SkipsWrite_EmitsWarnWithSkipped()
        {
            var store = new FakeKeyValueStore();
            var logSink = new FakeLogger();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "abcdefghij", logSink);
            var context = new FlagsEvaluationContext("user-1");

            var bigJson = "{\"data\":\"" + new string('x', 501 * 1024) + "\"}";

            cacheStore.Write(bigJson, context);

            Assert.AreEqual(0, store.SetStringCallCount, "SetString must NOT be called at 501 KB");
            Assert.AreEqual(0, store.SaveCallCount, "Save must NOT be called at 501 KB");
            Assert.IsTrue(logSink.WarnEmitted, "Warn log must be emitted at 501 KB");
            Assert.IsTrue(logSink.LastWarnMessage.Contains("skipped"), "Warn message must contain 'skipped'");
        }

        // ── Write: correct PlayerPrefs key ─────────────────────────────────

        [Test]
        public void Write_UsesComputedKey()
        {
            var store = new FakeKeyValueStore();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "abcdefghij", null);
            var context = new FlagsEvaluationContext("user-1");

            cacheStore.Write("{}", context);

            Assert.AreEqual("dd_flags_v1_us1_prod_cdefghij", store.LastSetStringKey);
        }

        // ── Write: null logger does not throw ──────────────────────────────

        [Test]
        public void Write_NullLogger_DoesNotThrow()
        {
            var store = new FakeKeyValueStore();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "token", null);
            var context = new FlagsEvaluationContext("user-1");

            Assert.DoesNotThrow(() => cacheStore.Write("{}", context));
        }

        // ── Write: exception in store does not propagate ───────────────────

        [Test]
        public void Write_StoreThrows_ExceptionCaughtAndWarnLogged()
        {
            var store = new ThrowingKeyValueStore();
            var logSink = new FakeLogger();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "token", logSink);
            var context = new FlagsEvaluationContext("user-1");

            Assert.DoesNotThrow(() => cacheStore.Write("{}", context));
            Assert.IsTrue(logSink.WarnEmitted, "Exception in store should emit a warn log");
        }

        // ── Write using DictionaryKeyValueStore (reusable test double) ────────

        [Test]
        public void Write_SmallPayload_DictionaryStore_HasOneEntry()
        {
            var store = new DictionaryKeyValueStore();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "abcdefghij", null);
            var context = new FlagsEvaluationContext("user-1");

            cacheStore.Write("{}", context);

            Assert.AreEqual(1, store.Store.Count, "Store must have exactly one entry after small write");
            var expectedKey = FlagsCacheStore.ComputeKey("us1", "prod", "abcdefghij");
            Assert.IsTrue(store.Store.ContainsKey(expectedKey), "Store key must match ComputeKey output");
        }

        [Test]
        public void Write_LargePayload_DictionaryStore_HasOneEntry_EmitsWarn()
        {
            var store = new DictionaryKeyValueStore();
            var logSink = new FakeLogger();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "abcdefghij", logSink);
            var context = new FlagsEvaluationContext("user-1");

            // 401 * 1024 raw bytes: envelope overhead pushes the serialized byte count above 400 KB.
            var bigJson = "{\"data\":\"" + new string('x', 401 * 1024) + "\"}";

            cacheStore.Write(bigJson, context);

            Assert.AreEqual(1, store.Store.Count, "Large payload within skip threshold must still write");
            Assert.IsTrue(logSink.WarnEmitted, "Warn log must be emitted for large payload");
            Assert.IsTrue(logSink.LastWarnMessage.Contains("large"), "Warn message must contain 'large'");
        }

        [Test]
        public void Write_OversizedPayload_DictionaryStore_IsEmpty()
        {
            var store = new DictionaryKeyValueStore();
            var logSink = new FakeLogger();
            var cacheStore = new FlagsCacheStore(store, "us1", "prod", "abcdefghij", logSink);
            var context = new FlagsEvaluationContext("user-1");

            // 501 * 1024 raw bytes: envelope byte count exceeds SkipThresholdBytes.
            var bigJson = "{\"data\":\"" + new string('x', 501 * 1024) + "\"}";

            cacheStore.Write(bigJson, context);

            Assert.AreEqual(0, store.Store.Count, "Oversized payload must not be written to store");
            Assert.IsTrue(logSink.WarnEmitted, "Warn log must be emitted when write is skipped");
            Assert.IsTrue(logSink.LastWarnMessage.Contains("skipped"), "Warn message must contain 'skipped'");
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private class FakeKeyValueStore : IKeyValueStore
        {
            public int SetStringCallCount { get; private set; }
            public int SaveCallCount { get; private set; }
            public string LastSetStringKey { get; private set; }

            public string GetString(string key, string defaultValue) => defaultValue;

            public void SetString(string key, string value)
            {
                SetStringCallCount++;
                LastSetStringKey = key;
            }

            public void DeleteKey(string key) { }

            public void Save() => SaveCallCount++;
        }

        private class ThrowingKeyValueStore : IKeyValueStore
        {
            public string GetString(string key, string defaultValue) => defaultValue;
            public void SetString(string key, string value) => throw new System.Exception("store exploded");
            public void DeleteKey(string key) { }
            public void Save() { }
        }

        private class FakeLogger : Datadog.Unity.Core.IInternalLogger
        {
            public bool WarnEmitted { get; private set; }
            public string LastWarnMessage { get; private set; }

            public void Log(DdLogLevel level, string message)
            {
                if (level == DdLogLevel.Warn)
                {
                    WarnEmitted = true;
                    LastWarnMessage = message;
                }
            }

            public void TelemetryDebug(string message) { }
            public void TelemetryError(string message, System.Exception exception) { }
        }
    }
}
