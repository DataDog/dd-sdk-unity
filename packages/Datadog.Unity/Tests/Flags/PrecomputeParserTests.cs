// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class PrecomputeParserTests
    {
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
