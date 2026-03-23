// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class FlagsEvaluationContextTests
    {
        [Test]
        public void NullTargetingKey_DefaultsToEmptyString()
        {
            var ctx = new FlagsEvaluationContext(null);
            Assert.AreEqual(string.Empty, ctx.TargetingKey);
        }

        [Test]
        public void NoAttributes_EmptyDictionary()
        {
            var ctx = new FlagsEvaluationContext("user-1");
            Assert.AreEqual(0, ctx.Attributes.Count);
        }

        [Test]
        public void PrimitiveAttributes_StoredAsStrings()
        {
            var ctx = new FlagsEvaluationContext("user-1", new Dictionary<string, object>
            {
                { "plan", "premium" },
                { "age", 42 },
                { "active", true },
            });

            Assert.AreEqual("premium", ctx.Attributes["plan"]);
            Assert.AreEqual("42", ctx.Attributes["age"]);
            Assert.AreEqual("True", ctx.Attributes["active"]);
        }

        [Test]
        public void NestedObject_FlattenedWithDotNotation()
        {
            var ctx = new FlagsEvaluationContext("user-1", new Dictionary<string, object>
            {
                {
                    "address", new Dictionary<string, object>
                    {
                        { "city", "New York" },
                        { "zip", "10001" },
                    }
                },
            });

            Assert.AreEqual("New York", ctx.Attributes["address.city"]);
            Assert.AreEqual("10001", ctx.Attributes["address.zip"]);
            Assert.IsFalse(ctx.Attributes.ContainsKey("address"));
        }

        [Test]
        public void DeeplyNested_FlattenedRecursively()
        {
            var ctx = new FlagsEvaluationContext("user-1", new Dictionary<string, object>
            {
                {
                    "a", new Dictionary<string, object>
                    {
                        {
                            "b", new Dictionary<string, object>
                            {
                                { "c", "deep" },
                            }
                        },
                    }
                },
            });

            Assert.AreEqual("deep", ctx.Attributes["a.b.c"]);
        }

        [Test]
        public void Attributes_AreImmutable()
        {
            var source = new Dictionary<string, object> { { "key", "value" } };
            var ctx = new FlagsEvaluationContext("user-1", source);

            // Mutating the source after construction should not affect the context
            source["key"] = "mutated";

            Assert.AreEqual("value", ctx.Attributes["key"]);
        }
    }
}
