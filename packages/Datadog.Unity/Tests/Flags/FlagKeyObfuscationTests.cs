// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.Collections.Generic;
using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class FlagKeyObfuscationTests
    {
        private const string Salt = "000102030405060708090a0b0c0d0e0f";

        private static JObject Descriptor(string salt = Salt) => new()
        {
            ["scheme"] = "flag-key-sha256-v1",
            ["salt"] = salt,
        };

        private static FlagKeyObfuscation Encoding(string salt = Salt) =>
            FlagKeyObfuscation.Read(new JValue(true), Descriptor(salt));

        private static JObject Assignment(string type = "boolean", JToken value = null) => new()
        {
            ["variationType"] = type,
            ["variationValue"] = value ?? new JValue(true),
            ["variationKey"] = "treatment",
            ["allocationKey"] = "allocation-123",
            ["reason"] = "TARGETING_MATCH",
            ["doLog"] = true,
        };

        private static JObject Response(JObject attributes) => new()
        {
            ["data"] = new JObject { ["attributes"] = attributes },
        };

        private static FlagAssignments Decode(string key, JObject assignment, string salt = Salt)
        {
            var attributes = new JObject
            {
                ["flags"] = new JObject { [Encoding(salt).Encode(key)] = assignment },
                ["obfuscated"] = true,
                ["obfuscation"] = Descriptor(salt),
            };
            return PrecomputeAssignmentsFetcher.ParseResponse(Response(attributes).ToString());
        }

        // Fixed vectors also run against the browser SDK and Rust edge encoder.
        [TestCase("new-route-planner", "a60479237ef2f69175bbe0bd581966d1583766941815dc1d414c883767795190")]
        [TestCase("Flag", "adca75d2141c51b0c0f084c1058edfb8e6e763f91aaf54ca06326d9847bd9586")]
        [TestCase("flag", "9817872c144b018abd77e3915bd77e2c27f4f534dccff8ffaca27361e3a5e1ee")]
        [TestCase(" flag ", "b1a5f851cc82a3fdf03a461d72a2241584dcf455df1d384e02a937901b3bc80b")]
        [TestCase("café", "3bfa8c3c17c1b61035b98ecf14007cdf5cba5ce61a48e8cfb65f8106541892d3")]
        [TestCase("cafe\u0301", "1e8b7ec5e8028a1ec96b38dd37f6ccea041c0a90358904af40ac5810c23c8765")]
        [TestCase("🚲/旗", "94b611e0d3b26b52f6ad66013d1c72f8a92ea109390049759e75d3c8d3aa4dab")]
        [TestCase("a\0b", "bac134d201be5e7f28fc7019248f0809c2a013b137e866446ee71add2bc344c7")]
        [TestCase("", "072d985b427f536ad0a11b2d4c5e0f7e6f0ff6d23e779e082f75599ce3fe3eba")]
        public void MatchesSharedHashVectors(string key, string expected)
        {
            Assert.AreEqual(expected, Encoding().Encode(key));
        }

        [Test]
        public void BoundsLookupHashesAndKeepsDescriptorsSeparate()
        {
            var encoding = Encoding();
            var other = Encoding(new string('f', 32));
            var first = encoding.Encode("flag");
            Assert.AreSame(first, encoding.Encode("flag"));
            Assert.AreNotEqual(first, other.Encode("flag"));
            Assert.AreSame(first, encoding.Encode("flag"));
            Assert.AreNotEqual(encoding.Encode("café"), encoding.Encode("cafe\u0301"));
            for (var index = 0; index < 1024; index++) encoding.Encode($"flag-{index}");
            var afterEviction = encoding.Encode("flag");
            Assert.AreEqual(first, afterEviction);
            Assert.AreNotSame(first, afterEviction);
        }

        [Test]
        public void RejectsInvalidUnicodeWithoutAliasingReplacementCharacter()
        {
            Assert.Throws<EncoderFallbackException>(() => Encoding().Encode("\ud800"));
            var repository = new FlagsRepository();
            repository.SetFlagsAndContext(new FlagsEvaluationContext("athlete"), Decode("\ufffd", Assignment()));
            Assert.IsNull(repository.GetFlagAssignment("\ud800"));
            Assert.IsNotNull(repository.GetFlagAssignment("\ufffd"));
        }

        [TestCase("{}")]
        [TestCase("{\"obfuscated\":false}")]
        public void AcceptsLegacyMetadata(string metadata)
        {
            var attributes = JObject.Parse(metadata);
            attributes["flags"] = new JObject { ["flag"] = Assignment() };
            var decoded = PrecomputeAssignmentsFetcher.ParseResponse(Response(attributes).ToString());
            Assert.IsNull(decoded.Obfuscation);
            Assert.IsTrue(decoded.Flags.ContainsKey("flag"));
        }

        [TestCase("{\"obfuscated\":true}")]
        [TestCase("{\"obfuscated\":null}")]
        [TestCase("{\"obfuscated\":\"true\"}")]
        [TestCase("{\"obfuscation\":null}")]
        [TestCase("{\"obfuscation\":{}}")]
        [TestCase("{\"obfuscated\":true,\"obfuscation\":null}")]
        [TestCase("{\"obfuscated\":true,\"obfuscation\":[]}")]
        [TestCase("{\"obfuscated\":true,\"obfuscation\":{\"scheme\":\"future\",\"salt\":\"000102030405060708090a0b0c0d0e0f\"}}")]
        [TestCase("{\"obfuscated\":false,\"obfuscation\":{\"scheme\":\"flag-key-sha256-v1\",\"salt\":\"000102030405060708090a0b0c0d0e0f\"}}")]
        public void RejectsMalformedMetadata(string metadata)
        {
            var attributes = JObject.Parse(metadata);
            attributes["flags"] = new JObject();
            Assert.Throws<JsonSerializationException>(() => PrecomputeAssignmentsFetcher.ParseResponse(Response(attributes).ToString()));
        }

        [TestCase("")]
        [TestCase("000102030405060708090a0b0c0d0e")]
        [TestCase("000102030405060708090a0b0c0d0e0f00")]
        [TestCase("000102030405060708090A0B0C0D0E0F")]
        [TestCase("000102030405060708090a0b0c0d0e0g")]
        [TestCase("000102030405060708090a0b0c0d0e0f\n")]
        [TestCase("000102030405060708090a0b0c0d0e0\n")]
        public void RejectsInvalidSalt(string salt)
        {
            Assert.Throws<JsonSerializationException>(() => Encoding(salt));
        }

        [Test]
        public void RejectsInvalidEncodedMapKey()
        {
            foreach (var key in new[] { "plaintext", new string('a', 63), new string('a', 65), new string('A', 64), new string('a', 64) + "\n", new string('a', 63) + "\n" })
            {
                var attributes = new JObject
                {
                    ["obfuscated"] = true, ["obfuscation"] = Descriptor(),
                    ["flags"] = new JObject { [key] = Assignment() },
                };
                Assert.Throws<JsonSerializationException>(() => PrecomputeAssignmentsFetcher.ParseResponse(Response(attributes).ToString()));
            }
        }

        [TestCase("boolean", "true")]
        [TestCase("string", "\"visible-value\"")]
        [TestCase("integer", "42")]
        [TestCase("number", "12.5")]
        [TestCase("object", "{\"visible\":[42,true]}")]
        public void PreservesAssignmentsAndChangesOnlyLookupKeys(string type, string value)
        {
            var assignment = Assignment(type, JToken.Parse(value));
            var plain = PrecomputeAssignmentsFetcher.ParseResponse(Response(new JObject
            {
                ["flags"] = new JObject { ["flag"] = assignment },
            }).ToString());
            var repository = new FlagsRepository();
            repository.SetFlagsAndContext(new FlagsEvaluationContext("athlete"), Decode("flag", assignment));
            var actual = repository.GetFlagAssignment("flag");
            var expected = plain.Flags["flag"];
            Assert.AreEqual(expected.VariationType, actual.VariationType);
            Assert.IsTrue(JToken.DeepEquals(expected.VariationValue, actual.VariationValue));
            Assert.AreEqual(expected.VariationKey, actual.VariationKey);
            Assert.AreEqual(expected.AllocationKey, actual.AllocationKey);
            Assert.AreEqual(expected.Reason, actual.Reason);
            Assert.AreEqual(expected.DoLog, actual.DoLog);
            Assert.IsNull(repository.GetFlagAssignment("missing"));
            Assert.IsNull(repository.GetFlagAssignment(Encoding().Encode("flag")), "No plaintext lookup fallback.");
        }

        [Test]
        public void AcceptsNewKeysAndFieldsAndPreservesUnknownVariantBehavior()
        {
            var encoding = Encoding();
            var attributes = new JObject
            {
                ["obfuscated"] = true, ["obfuscation"] = Descriptor(), ["future-field"] = true,
                ["flags"] = new JObject
                {
                    [encoding.Encode("flag")] = Assignment(),
                    [encoding.Encode("new-flag")] = Assignment(),
                    [encoding.Encode("future")] = Assignment("future-type"),
                },
            };
            var repository = new FlagsRepository();
            repository.SetFlagsAndContext(new FlagsEvaluationContext("athlete"),
                PrecomputeAssignmentsFetcher.ParseResponse(Response(attributes).ToString()));
            using var client = new FlagsClient(repository, null, null, null, null,
                false, false, null, FlagsClientState.Ready);
            Assert.IsTrue(client.GetBooleanValue("flag", false));
            Assert.IsTrue(client.GetBooleanValue("new-flag", false));
            // Unity converts the value without requiring a known variationType. Encoding must not change that.
            var plain = PrecomputeAssignmentsFetcher.ParseResponse(Response(new JObject
            {
                ["flags"] = new JObject { ["future"] = Assignment("future-type") },
            }).ToString());
            Assert.IsTrue(plain.Flags["future"].TryGetValue<bool>(out var expected));
            Assert.AreEqual(expected, client.GetBooleanValue("future", false));
        }

        [Test]
        public void RetainsOriginalDetailsAndExposureIdentityAcrossSaltChanges()
        {
            var repository = new FlagsRepository();
            var context = new FlagsEvaluationContext("athlete");
            var exposures = new List<ExposureEvent>();
            using var client = new FlagsClient(repository, new ExposureTracker(), null, null, null,
                true, false, exposures.Add, FlagsClientState.Ready);

            foreach (var salt in new[] { Salt, new string('f', 32), Salt })
            {
                repository.SetFlagsAndContext(context, Decode("flag", Assignment(), salt));
                var details = client.GetBooleanDetails("flag", false);
                Assert.IsTrue(details.Value);
                Assert.AreEqual("flag", details.Key);
                Assert.AreEqual("treatment", details.Variant);
                Assert.AreEqual("allocation-123", details.AllocationKey);
            }
            Assert.AreEqual(1, exposures.Count);
            StringAssert.Contains("\"key\":\"flag\"", JsonConvert.SerializeObject(exposures[0]));

            repository.SetFlagsAndContext(new FlagsEvaluationContext("other"), Decode("other-flag", Assignment()));
            Assert.IsNull(repository.GetFlagAssignment("flag"));
            Assert.IsNotNull(repository.GetFlagAssignment("other-flag"));
        }

        [Test]
        public void PreservesPublicApiDefaultsAndEvaluationTelemetry()
        {
            var repository = new FlagsRepository();
            repository.SetFlagsAndContext(new FlagsEvaluationContext("athlete"), Decode("flag", Assignment("string", new JValue("not-a-boolean"))));
            var events = new List<FlagEvaluationEvent>();
            var aggregator = new EvaluationAggregator(events.AddRange);
            using var client = new FlagsClient(repository, null, aggregator, null, null,
                false, true, null, FlagsClientState.Ready);
            Assert.AreEqual(FlagEvaluationError.TypeMismatch, client.GetBooleanDetails("flag", false).Error);
            Assert.AreEqual(FlagEvaluationError.FlagNotFound, client.GetBooleanDetails("missing", false).Error);
            client.Flush();
            Assert.AreEqual(2, events.Count);
            Assert.IsTrue(events.Exists(value => value.Flag.Key == "flag"));
            Assert.IsTrue(events.Exists(value => value.Flag.Key == "missing"));
        }

        [Test]
        public void AdvertisesAssignmentEncodingCapability()
        {
            var fetcher = new PrecomputeAssignmentsFetcher("https://example.invalid", "token", null, "prod", null);
            using var httpRequest = fetcher.BuildRequest(new FlagsEvaluationContext("athlete"));
            Assert.AreEqual("assignment-encoding-flag-key-256-v1", httpRequest.GetRequestHeader("X-DD-FEATURE-FLAGS-CAPABILITIES"));
            var request = JObject.Parse(fetcher.BuildRequestBody(new FlagsEvaluationContext("athlete")));
            var attributes = request["data"]["attributes"];
            Assert.IsNull(attributes["supported_capabilities"]);
            Assert.AreEqual("athlete", attributes["subject"]["targeting_key"].Value<string>());
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void InvalidEncodingRetainsOnlySameContextFallback(bool changeSubject, bool changeAttributes)
        {
            var repository = new FlagsRepository();
            var context = new FlagsEvaluationContext("athlete", new Dictionary<string, object> { ["country"] = "US" });
            repository.SetFlagsAndContext(context, Decode("flag", Assignment()));
            var invalid = Response(new JObject { ["obfuscated"] = true, ["flags"] = new JObject() }).ToString();
            using var client = new FlagsClient(repository, null, null, new ResponseFetcher(invalid), null,
                false, false, null, FlagsClientState.Ready);
            var next = new FlagsEvaluationContext(changeSubject ? "other" : "athlete",
                new Dictionary<string, object> { ["country"] = changeAttributes ? "FR" : "US" });
            var succeeded = true;
            client.SetEvaluationContext(next, value => succeeded = value);
            Assert.IsFalse(succeeded);
            Assert.AreEqual(!changeSubject && !changeAttributes, client.GetBooleanValue("flag", false));
            Assert.AreEqual(changeSubject || changeAttributes ? FlagsClientState.Error : FlagsClientState.Stale, client.State);
        }

        private sealed class ResponseFetcher : PrecomputeAssignmentsFetcher
        {
            private readonly string _response;
            internal ResponseFetcher(string response) : base("https://example.invalid", "token", null, "prod", null)
            {
                _response = response;
            }

            public override void Fetch(FlagsEvaluationContext context, Action<FlagAssignments> onComplete)
            {
                FlagAssignments assignments;
                try { assignments = ParseResponse(_response); }
                catch (JsonException) { assignments = null; }
                onComplete(assignments);
            }
        }
    }
}
