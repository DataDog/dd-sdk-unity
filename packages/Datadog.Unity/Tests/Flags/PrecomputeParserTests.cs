// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class PrecomputeParserTests
    {
        [TestCase("18446744073709551616", false)]
        [TestCase("-9223372036854775809", true)]
        [TestCase("9007199254740993", true)]
        [TestCase("9007199254740993.0", false)]
        public void NumericBoundariesDoNotThrow(string literal, bool doubleMismatch)
        {
            foreach (var type in new[] { "integer", "number", "float" })
            {
                var flags = ParseFlag(type, literal);
                Assert.IsFalse(flags["flag"].TryGetValue<int>(out _));
                Assert.AreEqual(!doubleMismatch, flags["flag"].TryGetValue<double>(out _));
            }
        }

        private static System.Collections.Generic.Dictionary<string, FlagAssignment> ParseFlag(string type, string value, string serial = "null")
        {
            return PrecomputeAssignmentsFetcher.ParseResponse(
                "{\"data\":{\"attributes\":{\"flags\":{\"flag\":{\"variationType\":\"" + type +
                "\",\"variationValue\":" + value + ",\"serialId\":" + serial + "}}}}}");
        }

        [TestCase("\"abc\"")]
        [TestCase("1e30")]
        [TestCase("1.5")]
        [TestCase("2.5")]
        [TestCase("9223372036854775808")]
        [TestCase("-9223372036854775809")]
        public void InvalidSerialIdDoesNotRemoveFlags(string serial)
        {
            var json = "{\"data\":{\"attributes\":{\"flags\":{" +
                "\"flag\":{\"variationType\":\"boolean\",\"variationValue\":true,\"serialId\":" + serial + "}," +
                "\"other\":{\"variationType\":\"string\",\"variationValue\":\"retained\"}}}}}";
            var flags = PrecomputeAssignmentsFetcher.ParseResponse(json);
            Assert.AreEqual(2, flags.Count);
            Assert.AreEqual("retained", (string)flags["other"].VariationValue);
            Assert.IsNull(flags["flag"].SerialId);
            Assert.IsTrue(flags["flag"].TryGetValue<bool>(out var value));
            Assert.IsTrue(value);
        }

        [TestCase("9223372036854775807", long.MaxValue)]
        [TestCase("-9223372036854775808", long.MinValue)]
        [TestCase("2.0", 2L)]
        public void ValidSerialIdIsRetained(string serial, long expected)
        {
            Assert.AreEqual(expected, ParseFlag("boolean", "true", serial)["flag"].SerialId);
        }

        [Test]
        public void PreservesTimestampStringsAndRejectsTrailingContent()
        {
            Assert.IsTrue(ParseFlag("string", "\"2026-01-01T00:00:00Z\"")["flag"].TryGetValue<string>(out var value));
            Assert.AreEqual("2026-01-01T00:00:00Z", value);
            var nested = ParseFlag("object", "{\"date\":\"2026-01-01T00:00:00Z\"}")["flag"].VariationValue;
            Assert.AreEqual(JTokenType.String, nested["date"].Type);
            Assert.AreEqual("2026-01-01T00:00:00Z", (string)nested["date"]);
            Assert.IsEmpty(PrecomputeAssignmentsFetcher.ParseResponse("{} {}"));
        }

        [Test]
        public void GenericNumericAndObjectReadsRemainSupported()
        {
            Assert.IsTrue(ParseFlag("number", "5000000000")["flag"].TryGetValue<long>(out var integer));
            Assert.AreEqual(5000000000L, integer);
            Assert.IsTrue(ParseFlag("number", "1.5")["flag"].TryGetValue<float>(out var number));
            Assert.AreEqual(1.5f, number);
            Assert.IsFalse(ParseFlag("number", "1.1")["flag"].TryGetValue<float>(out _));
            Assert.IsTrue(ParseFlag("number", "5000000000")["flag"].TryGetValue<object>(out var boxed));
            Assert.AreEqual(5000000000L, boxed);
            Assert.IsFalse(ParseFlag("number", "9223372036854775808")["flag"].TryGetValue<long>(out _));
        }

        [Test]
        public void PlaintextUnicodeStringsRemainDistinct()
        {
            ParseFlag("string", "\"caf\\u00e9\"")["flag"].TryGetValue<string>(out var composed);
            ParseFlag("string", "\"cafe\\u0301\"")["flag"].TryGetValue<string>(out var decomposed);
            Assert.AreEqual("café", composed);
            Assert.AreEqual("cafe\u0301", decomposed);
            Assert.AreNotEqual(composed, decomposed);
        }

        [Test]
        public void ParsesValidBooleanFlag()
        {
            var json = @"{
                ""data"": {
                    ""attributes"": {
                        ""flags"": {
                            ""enable-feature"": {
                                ""variationType"": ""boolean"",
                                ""variationValue"": true,
                                ""doLog"": true,
                                ""allocationKey"": ""alloc-abc"",
                                ""variationKey"": ""treatment"",
                                ""reason"": ""TARGETING_MATCH""
                            }
                        }
                    }
                }
            }";

            var flags = PrecomputeAssignmentsFetcher.ParseResponse(json);

            Assert.AreEqual(1, flags.Count);
            Assert.IsTrue(flags.ContainsKey("enable-feature"));

            var flag = flags["enable-feature"];
            Assert.AreEqual("boolean", flag.VariationType);
            Assert.AreEqual(true, flag.VariationValue.Value<bool>());
            Assert.IsTrue(flag.DoLog);
            Assert.AreEqual("alloc-abc", flag.AllocationKey);
            Assert.AreEqual("treatment", flag.VariationKey);
            Assert.AreEqual("TARGETING_MATCH", flag.Reason);
        }

        [Test]
        public void ParsesMultipleFlags()
        {
            var json = @"{
                ""data"": {
                    ""attributes"": {
                        ""flags"": {
                            ""flag-bool"": {
                                ""variationType"": ""boolean"",
                                ""variationValue"": false,
                                ""doLog"": true,
                                ""allocationKey"": ""a1"",
                                ""variationKey"": ""control"",
                                ""reason"": ""DEFAULT""
                            },
                            ""flag-string"": {
                                ""variationType"": ""string"",
                                ""variationValue"": ""hello"",
                                ""doLog"": false,
                                ""allocationKey"": ""a2"",
                                ""variationKey"": ""greeting"",
                                ""reason"": ""RULE_MATCH""
                            },
                            ""flag-int"": {
                                ""variationType"": ""integer"",
                                ""variationValue"": 42,
                                ""doLog"": true,
                                ""allocationKey"": ""a3"",
                                ""variationKey"": ""high"",
                                ""reason"": ""TARGETING_MATCH""
                            },
                            ""flag-double"": {
                                ""variationType"": ""number"",
                                ""variationValue"": 3.14,
                                ""doLog"": true,
                                ""allocationKey"": ""a4"",
                                ""variationKey"": ""pi"",
                                ""reason"": ""TARGETING_MATCH""
                            }
                        }
                    }
                }
            }";

            var flags = PrecomputeAssignmentsFetcher.ParseResponse(json);

            Assert.AreEqual(4, flags.Count);

            Assert.AreEqual(false, flags["flag-bool"].VariationValue.Value<bool>());
            Assert.IsFalse(flags["flag-string"].DoLog);

            Assert.IsTrue(flags["flag-int"].TryGetValue<int>(out var intVal));
            Assert.AreEqual(42, intVal);

            Assert.IsTrue(flags["flag-double"].TryGetValue<double>(out var dblVal));
            Assert.AreEqual(3.14, dblVal, 0.001);
        }

        [Test]
        public void ParsesEmptyFlagsResponse()
        {
            var json = @"{""data"":{""attributes"":{""flags"":{}}}}";
            var flags = PrecomputeAssignmentsFetcher.ParseResponse(json);
            Assert.AreEqual(0, flags.Count);
        }

        [Test]
        public void ParsesInvalidJsonReturnsEmptyDict()
        {
            var flags = PrecomputeAssignmentsFetcher.ParseResponse("not json");
            Assert.IsNotNull(flags);
            Assert.AreEqual(0, flags.Count);
        }

        [Test]
        public void ParsesNullJsonReturnsEmptyDict()
        {
            var flags = PrecomputeAssignmentsFetcher.ParseResponse(null);
            Assert.IsNotNull(flags);
            Assert.AreEqual(0, flags.Count);
        }
    }
}
