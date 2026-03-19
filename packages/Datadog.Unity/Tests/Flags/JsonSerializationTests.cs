// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Newtonsoft.Json;
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
                flag: new FlagRef("show-feature"),
                allocation: new FlagRef("alloc-123"),
                variant: new FlagRef("variant-a"),
                subject: new ExposureSubject(
                    id: "user-456",
                    attributes: new Dictionary<string, string>
                    {
                        { "email", "user@example.com" },
                        { "plan", "premium" },
                    }));

            var json = JsonConvert.SerializeObject(evt);

            Assert.IsTrue(json.Contains("\"timestamp\":1700000000000"));
            Assert.IsTrue(json.Contains("\"flag\":{\"key\":\"show-feature\"}"));
            Assert.IsTrue(json.Contains("\"allocation\":{\"key\":\"alloc-123\"}"));
            Assert.IsTrue(json.Contains("\"variant\":{\"key\":\"variant-a\"}"));
            Assert.IsTrue(json.Contains("\"id\":\"user-456\""));
            Assert.IsTrue(json.Contains("\"email\":\"user@example.com\""));
            Assert.IsTrue(json.Contains("\"plan\":\"premium\""));
        }

        [Test]
        public void ExposureEventOmitsNullAttributes()
        {
            var evt = new ExposureEvent(
                timestamp: 1700000000000,
                flag: new FlagRef("show-feature"),
                allocation: new FlagRef("alloc-123"),
                variant: new FlagRef("variant-a"),
                subject: new ExposureSubject(id: "user-456"));

            var json = JsonConvert.SerializeObject(evt);

            Assert.IsFalse(json.Contains("\"attributes\""));
        }

        [Test]
        public void FlagEvaluationEventSerializesCorrectly()
        {
            var evt = new FlagEvaluationEvent(
                timestamp: 1700000000000,
                flag: new FlagRef("my-flag"),
                firstEvaluation: 1700000000000,
                lastEvaluation: 1700000001000,
                evaluationCount: 5,
                variant: new FlagRef("treatment"),
                allocation: new FlagRef("alloc-1"),
                targetingKey: "user-789");

            var json = JsonConvert.SerializeObject(evt);

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
            var evt = new FlagEvaluationEvent(
                timestamp: 1700000000000,
                flag: new FlagRef("my-flag"),
                firstEvaluation: 1700000000000,
                lastEvaluation: 1700000000000,
                evaluationCount: 1,
                targetingKey: "user-789",
                runtimeDefaultUsed: true,
                error: new FlagErrorDetail("FLAG_NOT_FOUND"));

            var json = JsonConvert.SerializeObject(evt);

            Assert.IsFalse(json.Contains("\"variant\""));
            Assert.IsFalse(json.Contains("\"allocation\""));
            Assert.IsTrue(json.Contains("\"runtime_default_used\":true"));
            Assert.IsTrue(json.Contains("\"error\":{\"message\":\"FLAG_NOT_FOUND\"}"));
        }
    }
}
