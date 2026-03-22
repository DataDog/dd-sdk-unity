// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using OpenFeature;
using OpenFeature.Constant;

namespace Datadog.Unity.Flags.Tests
{
    public class FlagEvaluationTests
    {
        private DatadogFeatureProvider _provider;
        private FlagsClient _client;
        private FlagsRepository _repository;

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

            _provider = new DatadogFeatureProvider();
            _provider.SetClient(_client);
            Api.Instance.SetProviderAsync(_provider).GetAwaiter().GetResult();
        }

        [TearDown]
        public void TearDown()
        {
            _client?.Dispose();
        }

        [Test]
        public async Task BooleanFlagReturnsCorrectValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["show-feature"] = new FlagAssignment("boolean", true, true, "alloc-1", "treatment", "TARGETING_MATCH"),
            });

            var ofClient = Api.Instance.GetClient();
            var value = await ofClient.GetBooleanValueAsync("show-feature", false);

            Assert.IsTrue(value);
        }

        [Test]
        public async Task StringFlagReturnsCorrectValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["theme"] = new FlagAssignment("string", "dark", true, "alloc-1", "dark-mode", "TARGETING_MATCH"),
            });

            var ofClient = Api.Instance.GetClient();
            var value = await ofClient.GetStringValueAsync("theme", "light");

            Assert.AreEqual("dark", value);
        }

        [Test]
        public async Task IntegerFlagReturnsCorrectValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["max-items"] = new FlagAssignment("integer", 42, true, "alloc-1", "high", "TARGETING_MATCH"),
            });

            var ofClient = Api.Instance.GetClient();
            var value = await ofClient.GetIntegerValueAsync("max-items", 10);

            Assert.AreEqual(42, value);
        }

        [Test]
        public async Task DoubleFlagReturnsCorrectValue()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["price"] = new FlagAssignment("number", 9.99, true, "alloc-1", "discount", "TARGETING_MATCH"),
            });

            var ofClient = Api.Instance.GetClient();
            var value = await ofClient.GetDoubleValueAsync("price", 0.0);

            Assert.AreEqual(9.99, value, 0.001);
        }

        [Test]
        public async Task MissingFlagReturnsDefault()
        {
            SetFlags(new Dictionary<string, FlagAssignment>());

            var ofClient = Api.Instance.GetClient();
            var value = await ofClient.GetBooleanValueAsync("nonexistent", true);

            Assert.IsTrue(value);
        }

        [Test]
        public async Task MissingFlagReturnsErrorDetails()
        {
            SetFlags(new Dictionary<string, FlagAssignment>());

            var ofClient = Api.Instance.GetClient();
            var details = await ofClient.GetBooleanDetailsAsync("nonexistent", false);

            Assert.IsFalse(details.Value);
            Assert.AreEqual(ErrorType.FlagNotFound, details.ErrorType);
        }

        [Test]
        public async Task DetailsIncludeVariantAndReason()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["checkout-v2"] = new FlagAssignment("boolean", true, true, "alloc-1", "treatment", "TARGETING_MATCH"),
            });

            var ofClient = Api.Instance.GetClient();
            var details = await ofClient.GetBooleanDetailsAsync("checkout-v2", false);

            Assert.IsTrue(details.Value);
            Assert.AreEqual("treatment", details.Variant);
            Assert.AreEqual("TARGETING_MATCH", details.Reason);
            Assert.AreEqual(ErrorType.None, details.ErrorType);
        }

        [Test]
        public async Task SetFlagsReplacesExistingFlags()
        {
            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["flag-a"] = new FlagAssignment("boolean", true, true, "a", "v1", "DEFAULT"),
            });

            var ofClient = Api.Instance.GetClient();
            var a1 = await ofClient.GetBooleanValueAsync("flag-a", false);
            Assert.IsTrue(a1);

            SetFlags(new Dictionary<string, FlagAssignment>
            {
                ["flag-b"] = new FlagAssignment("string", "value", true, "b", "v2", "TARGETING_MATCH"),
            });

            var a2 = await ofClient.GetBooleanValueAsync("flag-a", false);
            Assert.IsFalse(a2); // gone, returns default

            var b = await ofClient.GetStringValueAsync("flag-b", "none");
            Assert.AreEqual("value", b);
        }

        private void SetFlags(Dictionary<string, FlagAssignment> flags)
        {
            _repository.SetFlagsAndContext(new FlagsEvaluationContext("user-1"), flags);
        }
    }
}
