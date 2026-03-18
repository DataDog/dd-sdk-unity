// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Flags;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Flags
{
    public class FlagsIntegrationTests
    {
        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(30);

        private MockServerHelper _mockServer;
        private string _mockBase;
        private string _precomputePayload;

        [SetUp]
        public void SetUp()
        {
            _mockServer = new MockServerHelper();
            _mockBase = DatadogConfigurationOptions.Load().CustomEndpoint;
            _precomputePayload = Resources.Load<TextAsset>("PrecomputePayload").text;
        }

        [TearDown]
        public void TearDown()
        {
            DdFlags.Shutdown();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private FlagsConfiguration MakeConfig(float flushInterval = 60f) => new FlagsConfiguration
        {
            TrackExposures = true,
            TrackEvaluations = true,
            EvaluationFlushIntervalSeconds = flushInterval,
            CustomFlagsEndpoint = $"{_mockBase}/precompute-assignments",
            CustomExposureEndpoint = $"{_mockBase}/api/v2/exposures",
            CustomEvaluationEndpoint = $"{_mockBase}/api/v2/flagevaluation",
        };

        private IEnumerator InitFlags(
            string targetingKey = "user-123",
            Dictionary<string, object> attributes = null,
            int precomputeStatus = 200,
            string precomputeBody = null,
            float flushInterval = 60f)
        {
            yield return _mockServer.Clear();
            yield return _mockServer.ConfigureResponse(
                "/precompute-assignments",
                precomputeStatus,
                precomputeBody ?? _precomputePayload);

            DdFlags.Enable(MakeConfig(flushInterval));
            DdFlags.CreateClient();

            var done = false;
            var context = attributes != null
                ? new FlagsEvaluationContext(targetingKey, attributes)
                : new FlagsEvaluationContext(targetingKey);

            DdFlags.SetEvaluationContext(context, _ => done = true);

            var deadline = DateTime.Now + TimeSpan.FromSeconds(20);
            yield return new WaitUntil(() => done || DateTime.Now > deadline);
        }

        // ─── Group 1: Precompute request shape ───────────────────────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator PrecomputeRequest_HasCorrectHeaders()
        {
            yield return InitFlags();

            MockServerRequest precomputeReq = null;
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                var endpoint = logs.FirstOrDefault(l => l.Endpoint.Contains("/precompute-assignments"));
                precomputeReq = endpoint?.Requests.FirstOrDefault();
                return precomputeReq != null;
            });

            Assert.IsNotNull(precomputeReq, "No request recorded to /precompute-assignments");
            var schema = precomputeReq.Schemas.FirstOrDefault();
            Assert.IsNotNull(schema, "No schema on precompute request");

            var headers = schema.ParsedHeaders;
            Assert.AreEqual("application/vnd.api+json", headers["Content-Type"]);
            Assert.AreEqual("fake-client-token", headers["dd-client-token"]);
            Assert.AreEqual("fake-rum-application-id", headers["dd-application-id"]);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator PrecomputeRequest_BodyHasCorrectJsonApiShape()
        {
            yield return InitFlags("user-123");

            MockServerRequest precomputeReq = null;
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                var endpoint = logs.FirstOrDefault(l => l.Endpoint.Contains("/precompute-assignments"));
                precomputeReq = endpoint?.Requests.FirstOrDefault();
                return precomputeReq != null;
            });

            Assert.IsNotNull(precomputeReq, "No request recorded to /precompute-assignments");
            var schema = precomputeReq.Schemas.FirstOrDefault();
            Assert.IsNotNull(schema);

            var body = JObject.Parse(schema.Data);
            Assert.AreEqual("precompute-assignments-request", (string)body["data"]["type"]);
            Assert.AreEqual("integration-test", (string)body["data"]["attributes"]["env"]["name"]);
            Assert.AreEqual("user-123", (string)body["data"]["attributes"]["subject"]["targeting_key"]);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator PrecomputeRequest_IncludesContextAttributes()
        {
            yield return InitFlags("user-123", new Dictionary<string, object> { { "plan", "premium" } });

            MockServerRequest precomputeReq = null;
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                var endpoint = logs.FirstOrDefault(l => l.Endpoint.Contains("/precompute-assignments"));
                precomputeReq = endpoint?.Requests.FirstOrDefault();
                return precomputeReq != null;
            });

            var schema = precomputeReq?.Schemas.FirstOrDefault();
            Assert.IsNotNull(schema);

            var body = JObject.Parse(schema.Data);
            Assert.AreEqual("premium", (string)body["data"]["attributes"]["subject"]["targeting_attributes"]["plan"]);
        }

        // ─── Group 2: Flags evaluable after fetch ────────────────────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator SetEvaluationContext_Success_FlagsAvailableViaClient()
        {
            yield return InitFlags("user-123");

            var client = DdFlags.GetClient();
            Assert.IsNotNull(client);
            Assert.AreEqual(FlagsClientState.Ready, client.State);

            Assert.IsTrue(client.GetBooleanValue("boolean-flag", false));
            Assert.AreEqual("red", client.GetStringValue("string-flag", "x"));
            Assert.AreEqual(42, client.GetIntegerValue("integer-flag", 0));
            Assert.AreEqual(3.14, client.GetDoubleValue("numeric-flag", 0.0), 0.001);

            var jsonDetails = client.GetDetails<object>("json-flag", null);
            Assert.AreEqual("variation-127", jsonDetails.Variant);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator SetEvaluationContext_ServerError_StateIsError()
        {
            yield return InitFlags(precomputeStatus: 500, precomputeBody: "{\"error\":\"server error\"}");

            var client = DdFlags.GetClient();
            Assert.IsNotNull(client);
            Assert.AreEqual(FlagsClientState.Error, client.State);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator SetEvaluationContext_ServerErrorAfterCache_StateIsStale()
        {
            // First fetch succeeds
            yield return InitFlags("user-123");
            var client = DdFlags.GetClient();
            Assert.AreEqual(FlagsClientState.Ready, client.State);

            // Reconfigure mock to return 500
            yield return _mockServer.ConfigureResponse("/precompute-assignments", 500, "{\"error\":\"server error\"}");

            var done = false;
            DdFlags.SetEvaluationContext(new FlagsEvaluationContext("user-456"), _ => done = true);
            yield return new WaitUntil(() => done);

            Assert.AreEqual(FlagsClientState.Stale, client.State);
            // Old flags still evaluable
            Assert.AreEqual("red", client.GetStringValue("string-flag", "default"));
        }

        // ─── Group 3: Exposure telemetry ─────────────────────────────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator BooleanFlagEvaluation_SendsExposureEvent()
        {
            yield return InitFlags("user-123");

            DdFlags.GetClient().GetBooleanValue("boolean-flag", false);

            var exposures = new List<ExposureEventDecoder>();
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                exposures = ExposureEventDecoder.FromMockServer(logs);
                return exposures.Count >= 1;
            });

            Assert.AreEqual(1, exposures.Count);
            var exp = exposures[0];
            Assert.AreEqual("boolean-flag", exp.FlagKey);
            Assert.AreEqual("allocation-124", exp.AllocationKey);
            Assert.AreEqual("variation-124", exp.VariantKey);
            Assert.AreEqual("user-123", exp.SubjectId);

            // Check headers on exposure request
            MockServerLog expEndpoint = null;
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                expEndpoint = logs.FirstOrDefault(l => l.Endpoint.Contains("/api/v2/exposures"));
                return expEndpoint != null;
            });

            Assert.IsNotNull(expEndpoint);
            var headers = expEndpoint.Requests[0].Schemas[0].ParsedHeaders;
            Assert.AreEqual("fake-client-token", headers["dd-api-key"]);
            Assert.AreEqual("unity", headers["dd-evp-origin"]);
            Assert.IsTrue(headers["Content-Type"].Contains("text/plain"));
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator SameFlag_EvaluatedMultipleTimes_SendsOnlyOneExposure()
        {
            yield return InitFlags("user-123");

            var flagsClient = DdFlags.GetClient();
            for (var i = 0; i < 5; i++)
            {
                flagsClient.GetBooleanValue("boolean-flag", false);
            }

            yield return new WaitForSeconds(2f);

            var exposures = new List<ExposureEventDecoder>();
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                exposures = ExposureEventDecoder.FromMockServer(logs);
                return exposures.Count >= 1;
            });

            Assert.AreEqual(1, exposures.Count, "Expected exactly one exposure for the same flag");
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator ContextChange_SendsFreshExposure()
        {
            // Evaluate for user-A
            yield return InitFlags("user-A");
            DdFlags.GetClient().GetBooleanValue("boolean-flag", false);

            // Change context to user-B (requires re-fetch)
            yield return _mockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);
            var done = false;
            DdFlags.SetEvaluationContext(new FlagsEvaluationContext("user-B"), _ => done = true);
            yield return new WaitUntil(() => done);

            DdFlags.GetClient().GetBooleanValue("boolean-flag", false);

            var exposures = new List<ExposureEventDecoder>();
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                exposures = ExposureEventDecoder.FromMockServer(logs);
                return exposures.Count >= 2;
            });

            Assert.AreEqual(2, exposures.Count, "Expected one exposure per context");
            var subjects = exposures.Select(e => e.SubjectId).ToList();
            CollectionAssert.Contains(subjects, "user-A");
            CollectionAssert.Contains(subjects, "user-B");
        }

        // ─── Group 4: Evaluation telemetry ───────────────────────────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator FlagEvaluation_AfterExplicitFlush_SendsEvaluationBatch()
        {
            yield return InitFlags("user-123");

            var flagsClient = DdFlags.GetClient();
            flagsClient.GetStringValue("string-flag", "x");
            flagsClient.GetStringValue("string-flag", "x");
            flagsClient.GetStringValue("string-flag", "x");
            flagsClient.Flush();

            List<BatchedEvaluations> batches = null;
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                batches = EvaluationEventDecoder.FromMockServer(logs);
                return batches.Count >= 1;
            });

            Assert.IsNotNull(batches);
            Assert.GreaterOrEqual(batches.Count, 1);

            var ctx = batches[0].Context;
            Assert.IsNotNull(ctx?.Env);
            Assert.IsNotNull(ctx?.Device);
            Assert.IsNotNull(ctx?.Os);

            var records = batches.SelectMany(b => b.FlagEvaluations).ToList();
            Assert.AreEqual(1, records.Count);
            var rec = records[0];
            Assert.AreEqual("string-flag", rec.FlagKey);
            Assert.AreEqual(3, rec.EvaluationCount);
            Assert.AreEqual("variation-123", rec.VariantKey);
            Assert.AreEqual("user-123", rec.TargetingKey);
            Assert.IsNull(rec.RuntimeDefaultUsed);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator MultipleFlags_ProduceSeparateEvaluationRecords()
        {
            yield return InitFlags("user-123");

            var flagsClient = DdFlags.GetClient();
            flagsClient.GetBooleanValue("boolean-flag", false);
            flagsClient.GetBooleanValue("boolean-flag", false);
            flagsClient.GetStringValue("string-flag", "x");
            flagsClient.Flush();

            var records = new List<EvaluationRecord>();
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                records = EvaluationEventDecoder.AllRecords(logs);
                return records.Count >= 2;
            });

            Assert.AreEqual(2, records.Count);
            var boolRec = records.FirstOrDefault(r => r.FlagKey == "boolean-flag");
            var strRec = records.FirstOrDefault(r => r.FlagKey == "string-flag");
            Assert.IsNotNull(boolRec);
            Assert.IsNotNull(strRec);
            Assert.AreEqual(2, boolRec.EvaluationCount);
            Assert.AreEqual(1, strRec.EvaluationCount);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator MissingFlag_EvaluationRecord_HasRuntimeDefaultUsedTrue()
        {
            yield return InitFlags("user-123");

            DdFlags.GetClient().GetStringValue("nonexistent-flag", "default");
            DdFlags.GetClient().Flush();

            var records = new List<EvaluationRecord>();
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                records = EvaluationEventDecoder.AllRecords(logs);
                return records.Count >= 1;
            });

            Assert.AreEqual(1, records.Count);
            var rec = records[0];
            Assert.AreEqual("nonexistent-flag", rec.FlagKey);
            Assert.IsTrue(rec.RuntimeDefaultUsed == true);
            Assert.AreEqual("FLAG_NOT_FOUND", rec.ErrorMessage);
            Assert.IsNull(rec.VariantKey);
            Assert.IsNull(rec.AllocationKey);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator Shutdown_FlushesEvaluationsPendingOnTimer()
        {
            yield return InitFlags("user-123", flushInterval: 60f);

            DdFlags.GetClient().GetStringValue("string-flag", "x");

            // Shutdown should flush pending evaluations before destroying the aggregator
            DdFlags.Shutdown();

            var records = new List<EvaluationRecord>();
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                records = EvaluationEventDecoder.AllRecords(logs);
                return records.Count >= 1;
            });

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("string-flag", records[0].FlagKey);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator TimerFlush_SendsEvaluationBatchAfterInterval()
        {
            yield return InitFlags("user-123", flushInterval: 1.0f);

            DdFlags.GetClient().GetStringValue("string-flag", "x");

            // Wait for the timer to fire (interval = 1s, wait a bit longer)
            yield return new WaitForSeconds(2.0f);

            var records = new List<EvaluationRecord>();
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                records = EvaluationEventDecoder.AllRecords(logs);
                return records.Count >= 1;
            });

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("string-flag", records[0].FlagKey);
        }

        // ─── Group 5: Evaluation EVP headers ─────────────────────────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator EvaluationBatch_HasCorrectEvpHeaders()
        {
            yield return InitFlags("user-123");

            DdFlags.GetClient().GetStringValue("string-flag", "x");
            DdFlags.GetClient().Flush();

            MockServerLog evalEndpoint = null;
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                evalEndpoint = logs.FirstOrDefault(l => l.Endpoint.Contains("/api/v2/flagevaluation"));
                return evalEndpoint != null;
            });

            Assert.IsNotNull(evalEndpoint);
            var headers = evalEndpoint.Requests[0].Schemas[0].ParsedHeaders;
            Assert.AreEqual("fake-client-token", headers["dd-api-key"]);
            Assert.AreEqual("unity", headers["dd-evp-origin"]);
            Assert.AreEqual(DatadogSdk.SdkVersion, headers["dd-evp-origin-version"]);
            Assert.IsTrue(headers["Content-Type"].Contains("application/json"));
        }
    }
}
