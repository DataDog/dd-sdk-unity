// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Datadog.Unity.Flags;
using NUnit.Framework;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace Datadog.Unity.Flags.Tests
{
    public class DatadogFeatureProviderTests
    {
        private FlagsRepository _repository;
        private FlagsClient _client;
        private DatadogFeatureProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _repository = new FlagsRepository();
            _client = new FlagsClient(
                repository: _repository,
                exposureTracker: new ExposureTracker(),
                evaluationAggregator: null,
                fetcher: null,
                logger: null,
                trackExposures: false,
                trackEvaluations: false,
                onExposure: null);
            // Use internal constructor to inject a specific client for testing
            _provider = new DatadogFeatureProvider(_client);
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
        }

        // ─── Metadata ─────────────────────────────────────────────────────────────

        [Test]
        public void GetMetadata_ReturnsDatadogName()
        {
            Assert.AreEqual("Datadog", _provider.GetMetadata().Name);
        }

        // ─── ProviderNotReady (client has no context yet) ──────────────────────────

        [Test]
        public async Task ResolveBooleanValue_WhenClientNotReady_ReturnsProviderNotReadyError()
        {
            // No SetEvaluationContext called — client is in NotReady state, flag lookup returns ProviderNotReady
            var result = await _provider.ResolveBooleanValueAsync("flag", false);

            Assert.IsFalse(result.Value);
            Assert.AreEqual(ErrorType.ProviderNotReady, result.ErrorType);
        }

        // ─── Boolean resolution ────────────────────────────────────────────────────

        [Test]
        public async Task ResolveBooleanValue_WithValidFlag_ReturnsValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("boolean", true, true, "alloc-1", "variant-on", "TARGETING_MATCH"),
            });

            var result = await _provider.ResolveBooleanValueAsync("my-flag", false);

            Assert.IsTrue(result.Value);
            Assert.AreEqual("variant-on", result.Variant);
            Assert.AreEqual("TARGETING_MATCH", result.Reason);
            Assert.AreEqual(ErrorType.None, result.ErrorType);
        }

        [Test]
        public async Task ResolveBooleanValue_WithMissingFlag_ReturnsFlagNotFoundError()
        {
            SetFlags(new Dictionary<string, FlagAssignment>());

            var result = await _provider.ResolveBooleanValueAsync("nonexistent", false);

            Assert.IsFalse(result.Value);
            Assert.AreEqual(ErrorType.FlagNotFound, result.ErrorType);
        }

        [Test]
        public async Task ResolveBooleanValue_WithTypeMismatch_ReturnsTypeMismatchError()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("string", "hello", true, "alloc-1", "variant-1", "TARGETING_MATCH"),
            });

            var result = await _provider.ResolveBooleanValueAsync("my-flag", false);

            Assert.IsFalse(result.Value);
            Assert.AreEqual(ErrorType.TypeMismatch, result.ErrorType);
        }

        // ─── String resolution ─────────────────────────────────────────────────────

        [Test]
        public async Task ResolveStringValue_WithValidFlag_ReturnsValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["theme"] = new FlagAssignment("string", "dark", true, "alloc-1", "dark-mode", "TARGETING_MATCH"),
            });

            var result = await _provider.ResolveStringValueAsync("theme", "light");

            Assert.AreEqual("dark", result.Value);
            Assert.AreEqual("dark-mode", result.Variant);
            Assert.AreEqual(ErrorType.None, result.ErrorType);
        }

        [Test]
        public async Task ResolveStringValue_WithMissingFlag_ReturnsFlagNotFoundError()
        {
            SetFlags(new Dictionary<string, FlagAssignment>());

            var result = await _provider.ResolveStringValueAsync("nonexistent", "default");

            Assert.AreEqual("default", result.Value);
            Assert.AreEqual(ErrorType.FlagNotFound, result.ErrorType);
        }

        // ─── Integer resolution ────────────────────────────────────────────────────

        [Test]
        public async Task ResolveIntegerValue_WithValidFlag_ReturnsValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["max-items"] = new FlagAssignment("integer", 10, true, "alloc-1", "high", "TARGETING_MATCH"),
            });

            var result = await _provider.ResolveIntegerValueAsync("max-items", 5);

            Assert.AreEqual(10, result.Value);
            Assert.AreEqual(ErrorType.None, result.ErrorType);
        }

        // ─── Double resolution ─────────────────────────────────────────────────────

        [Test]
        public async Task ResolveDoubleValue_WithValidFlag_ReturnsValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["discount"] = new FlagAssignment("number", 0.15, true, "alloc-1", "promo", "TARGETING_MATCH"),
            });

            var result = await _provider.ResolveDoubleValueAsync("discount", 0.0);

            Assert.AreEqual(0.15, result.Value, 0.001);
            Assert.AreEqual(ErrorType.None, result.ErrorType);
        }

        // ─── Structure resolution ──────────────────────────────────────────────────

        [Test]
        public async Task ResolveStructureValue_WithDictionaryFlag_ReturnsStructure()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["config"] = new FlagAssignment("json", Newtonsoft.Json.Linq.JToken.FromObject(new Dictionary<string, object>
                {
                    { "color", "red" },
                    { "size", 42 },
                }), true, "alloc-1", "v1", "TARGETING_MATCH"),
            });

            var result = await _provider.ResolveStructureValueAsync("config", null);

            Assert.AreEqual(ErrorType.None, result.ErrorType);
            Assert.IsNotNull(result.Value);
            Assert.AreEqual("red", result.Value.AsStructure.GetValue("color").AsString);
            Assert.AreEqual(42, result.Value.AsStructure.GetValue("size").AsInteger);
        }

        [Test]
        public async Task ResolveStructureValue_WithMissingFlag_ReturnsFlagNotFoundError()
        {
            SetFlags(new Dictionary<string, FlagAssignment>());

            var result = await _provider.ResolveStructureValueAsync("nonexistent", null);

            Assert.IsNull(result.Value);
            Assert.AreEqual(ErrorType.FlagNotFound, result.ErrorType);
        }

        // ─── Thread safety ─────────────────────────────────────────────────────────

        [Test]
        public void ConcurrentResolve_IsThreadSafe()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["flag"] = new FlagAssignment("boolean", true, true, "alloc-1", "on", "TARGETING_MATCH"),
            });

            var errors = new System.Collections.Concurrent.ConcurrentBag<System.Exception>();
            var threads = new System.Threading.Thread[10];
            for (int i = 0; i < threads.Length; i++)
            {
                threads[i] = new System.Threading.Thread(() =>
                {
                    try
                    {
                        _ = _provider.ResolveBooleanValueAsync("flag", false).GetAwaiter().GetResult();
                    }
                    catch (System.Exception ex)
                    {
                        errors.Add(ex);
                    }
                });
            }

            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();

            Assert.IsEmpty(errors, "Thread safety violation detected");
        }

        // ─── Helpers ───────────────────────────────────────────────────────────────

        private void SetFlags(Dictionary<string, FlagAssignment> flags)
        {
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("test-user"), flags);
        }
    }
}
