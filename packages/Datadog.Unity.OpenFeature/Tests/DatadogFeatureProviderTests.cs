// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Threading;
using Datadog.Unity.Flags;
using NSubstitute;
using NUnit.Framework;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace Datadog.Unity.Flags.OpenFeature.Tests
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
        public void ResolveBooleanValue_WhenClientNotReady_ReturnsProviderNotReadyError()
        {
            // No SetEvaluationContext called — client is in NotReady state, flag lookup returns ProviderNotReady
            var result = _provider.ResolveBooleanValueAsync("flag", false).GetAwaiter().GetResult();

            Assert.IsFalse(result.Value);
            Assert.AreEqual(ErrorType.ProviderNotReady, result.ErrorType);
        }

        // ─── Boolean resolution ────────────────────────────────────────────────────

        [Test]
        public void ResolveBooleanValue_WithValidFlag_ReturnsValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("boolean", true, true, "alloc-1", "variant-on", "TARGETING_MATCH"),
            });

            var result = _provider.ResolveBooleanValueAsync("my-flag", false).GetAwaiter().GetResult();

            Assert.IsTrue(result.Value);
            Assert.AreEqual("variant-on", result.Variant);
            Assert.AreEqual("TARGETING_MATCH", result.Reason);
            Assert.AreEqual(ErrorType.None, result.ErrorType);
        }

        [Test]
        public void ResolveBooleanValue_WithMissingFlag_ReturnsFlagNotFoundError()
        {
            var provider = ProviderWithReadyClient(new Dictionary<string, FlagAssignment>());

            var result = provider.ResolveBooleanValueAsync("nonexistent", false).GetAwaiter().GetResult();

            Assert.IsFalse(result.Value);
            Assert.AreEqual(ErrorType.FlagNotFound, result.ErrorType);
        }

        [Test]
        public void ResolveBooleanValue_WithTypeMismatch_ReturnsTypeMismatchError()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("string", "hello", true, "alloc-1", "variant-1", "TARGETING_MATCH"),
            });

            var result = _provider.ResolveBooleanValueAsync("my-flag", false).GetAwaiter().GetResult();

            Assert.IsFalse(result.Value);
            Assert.AreEqual(ErrorType.TypeMismatch, result.ErrorType);
        }

        // ─── String resolution ─────────────────────────────────────────────────────

        [Test]
        public void ResolveStringValue_WithValidFlag_ReturnsValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["theme"] = new FlagAssignment("string", "dark", true, "alloc-1", "dark-mode", "TARGETING_MATCH"),
            });

            var result = _provider.ResolveStringValueAsync("theme", "light").GetAwaiter().GetResult();

            Assert.AreEqual("dark", result.Value);
            Assert.AreEqual("dark-mode", result.Variant);
            Assert.AreEqual(ErrorType.None, result.ErrorType);
        }

        [Test]
        public void ResolveStringValue_WithMissingFlag_ReturnsFlagNotFoundError()
        {
            var provider = ProviderWithReadyClient(new Dictionary<string, FlagAssignment>());

            var result = provider.ResolveStringValueAsync("nonexistent", "default").GetAwaiter().GetResult();

            Assert.AreEqual("default", result.Value);
            Assert.AreEqual(ErrorType.FlagNotFound, result.ErrorType);
        }

        // ─── Integer resolution ────────────────────────────────────────────────────

        [Test]
        public void ResolveIntegerValue_WithValidFlag_ReturnsValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["max-items"] = new FlagAssignment("integer", 10, true, "alloc-1", "high", "TARGETING_MATCH"),
            });

            var result = _provider.ResolveIntegerValueAsync("max-items", 5).GetAwaiter().GetResult();

            Assert.AreEqual(10, result.Value);
            Assert.AreEqual(ErrorType.None, result.ErrorType);
        }

        // ─── Double resolution ─────────────────────────────────────────────────────

        [Test]
        public void ResolveDoubleValue_WithValidFlag_ReturnsValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["discount"] = new FlagAssignment("number", 0.15, true, "alloc-1", "promo", "TARGETING_MATCH"),
            });

            var result = _provider.ResolveDoubleValueAsync("discount", 0.0).GetAwaiter().GetResult();

            Assert.AreEqual(0.15, result.Value, 0.001);
            Assert.AreEqual(ErrorType.None, result.ErrorType);
        }

        // ─── Structure resolution ──────────────────────────────────────────────────

        [Test]
        public void ResolveStructureValue_WithDictionaryFlag_ReturnsStructure()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["config"] = new FlagAssignment("json", Newtonsoft.Json.Linq.JToken.FromObject(new Dictionary<string, object>
                {
                    { "color", "red" },
                    { "size", 42 },
                }), true, "alloc-1", "v1", "TARGETING_MATCH"),
            });

            var result = _provider.ResolveStructureValueAsync("config", null).GetAwaiter().GetResult();

            Assert.AreEqual(ErrorType.None, result.ErrorType);
            Assert.IsNotNull(result.Value);
            Assert.AreEqual("red", result.Value.AsStructure.GetValue("color").AsString);
            Assert.AreEqual(42, result.Value.AsStructure.GetValue("size").AsInteger);
        }

        [Test]
        public void ResolveStructureValue_WithMissingFlag_ReturnsFlagNotFoundError()
        {
            var provider = ProviderWithReadyClient(new Dictionary<string, FlagAssignment>());

            var result = provider.ResolveStructureValueAsync("nonexistent", null).GetAwaiter().GetResult();

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

        // ─── Context-aware resolution (hybrid workaround) ─────────────────────────

        [Test]
        public void ResolveBooleanValue_WithNewContext_CallsSetEvaluationContext()
        {
            var mockClient = Substitute.For<IFlagsClient>();
            mockClient.When(c => c.SetEvaluationContext(
                    Arg.Any<FlagsEvaluationContext>(),
                    Arg.Any<Action<bool>>()))
                .Do(ci => ci.ArgAt<Action<bool>>(1)?.Invoke(true));
            mockClient.GetDetails("my-flag", false)
                .Returns(new FlagDetails<bool>("my-flag", true, variant: "on", reason: "TARGETING_MATCH"));

            var provider = new DatadogFeatureProvider(mockClient);
            var context = EvaluationContext.Builder().SetTargetingKey("user-123").Build();

            var result = provider.ResolveBooleanValueAsync("my-flag", false, context).GetAwaiter().GetResult();

            mockClient.Received(1).SetEvaluationContext(
                Arg.Is<FlagsEvaluationContext>(c => c.TargetingKey == "user-123"),
                Arg.Any<Action<bool>>());
            Assert.IsTrue(result.Value);
        }

        [Test]
        public void ResolveBooleanValue_WithSameContextTwice_CallsSetEvaluationContextOnce()
        {
            var mockClient = Substitute.For<IFlagsClient>();
            mockClient.When(c => c.SetEvaluationContext(
                    Arg.Any<FlagsEvaluationContext>(),
                    Arg.Any<Action<bool>>()))
                .Do(ci => ci.ArgAt<Action<bool>>(1)?.Invoke(true));
            mockClient.GetDetails("my-flag", false)
                .Returns(new FlagDetails<bool>("my-flag", true, variant: "on", reason: "TARGETING_MATCH"));

            var provider = new DatadogFeatureProvider(mockClient);
            var context = EvaluationContext.Builder().SetTargetingKey("user-123").Build();

            provider.ResolveBooleanValueAsync("my-flag", false, context).GetAwaiter().GetResult();
            provider.ResolveBooleanValueAsync("my-flag", false, context).GetAwaiter().GetResult();

            mockClient.Received(1).SetEvaluationContext(
                Arg.Any<FlagsEvaluationContext>(),
                Arg.Any<Action<bool>>());
        }

        [Test]
        public void ResolveBooleanValue_WithChangedContext_CallsSetEvaluationContextAgain()
        {
            var mockClient = Substitute.For<IFlagsClient>();
            mockClient.When(c => c.SetEvaluationContext(
                    Arg.Any<FlagsEvaluationContext>(),
                    Arg.Any<Action<bool>>()))
                .Do(ci => ci.ArgAt<Action<bool>>(1)?.Invoke(true));
            mockClient.GetDetails("my-flag", false)
                .Returns(new FlagDetails<bool>("my-flag", true, variant: "on", reason: "TARGETING_MATCH"));

            var provider = new DatadogFeatureProvider(mockClient);
            var ctx1 = EvaluationContext.Builder().SetTargetingKey("user-A").Build();
            var ctx2 = EvaluationContext.Builder().SetTargetingKey("user-B").Build();

            provider.ResolveBooleanValueAsync("my-flag", false, ctx1).GetAwaiter().GetResult();
            provider.ResolveBooleanValueAsync("my-flag", false, ctx2).GetAwaiter().GetResult();

            mockClient.Received(1).SetEvaluationContext(
                Arg.Is<FlagsEvaluationContext>(c => c.TargetingKey == "user-A"),
                Arg.Any<Action<bool>>());
            mockClient.Received(1).SetEvaluationContext(
                Arg.Is<FlagsEvaluationContext>(c => c.TargetingKey == "user-B"),
                Arg.Any<Action<bool>>());
        }

        [Test]
        public void ResolveBooleanValue_WithNullContext_DoesNotCallSetEvaluationContext()
        {
            var mockClient = Substitute.For<IFlagsClient>();
            mockClient.GetDetails("my-flag", false)
                .Returns(new FlagDetails<bool>("my-flag", false, error: FlagEvaluationError.ProviderNotReady));

            var provider = new DatadogFeatureProvider(mockClient);

            provider.ResolveBooleanValueAsync("my-flag", false, null).GetAwaiter().GetResult();

            mockClient.DidNotReceive().SetEvaluationContext(
                Arg.Any<FlagsEvaluationContext>(),
                Arg.Any<Action<bool>>());
        }

        // ─── FlagMetadata threading ────────────────────────────────────────────────

        [Test]
        public void ResolveBooleanValue_WithMetadata_ThreadsMetadataIntoFlagMetadata()
        {
            var extraLogging = new Newtonsoft.Json.Linq.JObject
            {
                ["experiment"] = "exp-1",
                ["sampleRate"] = 0.5,
            };
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("boolean", true, true, "alloc-42", "variant-on", "TARGETING_MATCH", extraLogging),
            });

            var result = _provider.ResolveBooleanValueAsync("my-flag", false).GetAwaiter().GetResult();

            Assert.AreEqual("alloc-42", result.FlagMetadata?.GetString("allocationKey"));
            Assert.AreEqual("exp-1", result.FlagMetadata?.GetString("experiment"));
            Assert.AreEqual(0.5, result.FlagMetadata?.GetDouble("sampleRate"));
        }

        [Test]
        public void ResolveBooleanValue_WithNoExtraLoggingAndBlankAllocationKey_HasNullFlagMetadata()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["my-flag"] = new FlagAssignment("boolean", true, true, "", "variant-on", "TARGETING_MATCH"),
            });

            var result = _provider.ResolveBooleanValueAsync("my-flag", false).GetAwaiter().GetResult();

            Assert.IsNull(result.FlagMetadata);
        }

        [Test]
        public void ResolveBooleanValue_ErrorPath_HasNullFlagMetadata()
        {
            // Error paths do not populate FlagMetadata
            var result = _provider.ResolveBooleanValueAsync("missing-flag", false).GetAwaiter().GetResult();

            Assert.IsNull(result.FlagMetadata);
        }

        // ─── Helpers ───────────────────────────────────────────────────────────────

        private void SetFlags(Dictionary<string, FlagAssignment> flags)
        {
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("test-user"), flags);
        }

        /// <summary>
        /// Returns a provider backed by a client in Ready state with the given flags.
        /// Used by tests that need to distinguish FlagNotFound from ProviderNotReady.
        /// </summary>
        private DatadogFeatureProvider ProviderWithReadyClient(Dictionary<string, FlagAssignment> flags)
        {
            var repo = new FlagsRepository();
            repo.SetFlagsAndContext(new FlagsEvaluationContext("test-user"), flags);
            var client = new FlagsClient(
                repository: repo,
                exposureTracker: new ExposureTracker(),
                evaluationAggregator: null,
                fetcher: null,
                logger: null,
                trackExposures: false,
                trackEvaluations: false,
                onExposure: null,
                initialState: FlagsClientState.Ready);
            return new DatadogFeatureProvider(client);
        }
    }
}
