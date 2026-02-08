// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class JsonSerializationTests
    {
        [Test]
        public void ExposureEventSerializesCorrectly()
        {
            var evt = new ExposureEvent(
                timestamp: 1700000000000,
                flagKey: "show-feature",
                allocationKey: "alloc-123",
                variationKey: "variant-a",
                subjectId: "user-456",
                subjectAttributes: new Dictionary<string, object>
                {
                    { "email", "user@example.com" },
                    { "plan", "premium" },
                });

            var json = evt.ToJson();

            Assert.IsTrue(json.Contains("\"timestamp\":1700000000000"));
            Assert.IsTrue(json.Contains("\"flag\":{\"key\":\"show-feature\"}"));
            Assert.IsTrue(json.Contains("\"allocation\":{\"key\":\"alloc-123\"}"));
            Assert.IsTrue(json.Contains("\"variant\":{\"key\":\"variant-a\"}"));
            Assert.IsTrue(json.Contains("\"id\":\"user-456\""));
            Assert.IsTrue(json.Contains("\"email\":\"user@example.com\""));
            Assert.IsTrue(json.Contains("\"plan\":\"premium\""));
        }

        [Test]
        public void FlagEvaluationEventSerializesCorrectly()
        {
            var evt = new FlagEvaluationEvent
            {
                Timestamp = 1700000000000,
                FlagKey = "my-flag",
                FirstEvaluation = 1700000000000,
                LastEvaluation = 1700000001000,
                EvaluationCount = 5,
                VariantKey = "treatment",
                AllocationKey = "alloc-1",
                TargetingKey = "user-789",
                RuntimeDefaultUsed = null,
            };

            var json = evt.ToJson();

            Assert.IsTrue(json.Contains("\"timestamp\":1700000000000"));
            Assert.IsTrue(json.Contains("\"flag\":{\"key\":\"my-flag\"}"));
            Assert.IsTrue(json.Contains("\"first_evaluation\":1700000000000"));
            Assert.IsTrue(json.Contains("\"last_evaluation\":1700000001000"));
            Assert.IsTrue(json.Contains("\"evaluation_count\":5"));
            Assert.IsTrue(json.Contains("\"variant\":{\"key\":\"treatment\"}"));
            Assert.IsTrue(json.Contains("\"allocation\":{\"key\":\"alloc-1\"}"));
            Assert.IsTrue(json.Contains("\"targeting_key\":\"user-789\""));
            Assert.IsFalse(json.Contains("\"runtime_default_used\""));
        }

        [Test]
        public void RuntimeDefaultOmitsVariantAndAllocation()
        {
            var evt = new FlagEvaluationEvent
            {
                Timestamp = 1700000000000,
                FlagKey = "my-flag",
                FirstEvaluation = 1700000000000,
                LastEvaluation = 1700000000000,
                EvaluationCount = 1,
                VariantKey = "treatment",
                AllocationKey = "alloc-1",
                TargetingKey = "user-789",
                RuntimeDefaultUsed = true,
                ErrorMessage = "FLAG_NOT_FOUND",
            };

            var json = evt.ToJson();

            Assert.IsFalse(json.Contains("\"variant\""));
            Assert.IsFalse(json.Contains("\"allocation\""));
            Assert.IsTrue(json.Contains("\"runtime_default_used\":true"));
            Assert.IsTrue(json.Contains("\"error\":{\"message\":\"FLAG_NOT_FOUND\"}"));
        }

        [Test]
        public void JsonEscapesSpecialCharacters()
        {
            var escaped = JsonHelper.Escape("hello \"world\"\nnewline");
            Assert.AreEqual("\"hello \\\"world\\\"\\nnewline\"", escaped);
        }

        [Test]
        public void JsonEscapesNullToLiteral()
        {
            var escaped = JsonHelper.Escape(null);
            Assert.AreEqual("null", escaped);
        }
    }
}
