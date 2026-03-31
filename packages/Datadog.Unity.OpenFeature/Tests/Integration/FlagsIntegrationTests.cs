// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Datadog.Unity.Flags;
using Datadog.Unity.Flags.OpenFeature.Tests.Integration.Decoders;
using Datadog.Unity.Tests.Integration;
using OpenFeature.Constant;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OpenFeature;
using OpenFeature.Model;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Flags.OpenFeature.Tests.Integration
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
            Api.Instance.ShutdownAsync().GetAwaiter().GetResult();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────

        private FlagsConfiguration MakeConfig(
            float flushInterval = 60f,
            bool trackExposures = true,
            bool trackEvaluations = true) => new FlagsConfiguration(
                evaluationFlushIntervalSeconds: flushInterval,
                trackExposures: trackExposures,
                trackEvaluations: trackEvaluations,
                customFlagsEndpoint: $"{_mockBase}/precompute-assignments",
                customExposureEndpoint: $"{_mockBase}/api/v2/exposures",
                customEvaluationEndpoint: $"{_mockBase}/api/v2/flagevaluation");

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
            var client = DdFlags.Instance.CreateClient();

            // Register provider with OpenFeature (user's responsibility in production)
            yield return AwaitTask(Api.Instance.SetProviderAsync(new DatadogFeatureProvider(DdFlags.Instance.GetClient())));

            var done = false;
            var context = attributes != null
                ? new FlagsEvaluationContext(targetingKey, attributes)
                : new FlagsEvaluationContext(targetingKey);

            client.SetEvaluationContext(context, _ => done = true);

            var deadline = DateTime.Now + TimeSpan.FromSeconds(20);
            yield return new WaitUntil(() => done || DateTime.Now > deadline);
        }

        /// <summary>Bridges an async Task into a Unity coroutine.</summary>
        private IEnumerator AwaitTask(Task task)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
            if (task.IsFaulted)
            {
                throw task.Exception.GetBaseException();
            }
        }

        /// <summary>Bridges an async Task&lt;T&gt; into a Unity coroutine, capturing the result.</summary>
        private IEnumerator AwaitTask<T>(Task<T> task, Action<T> onResult)
        {
            while (!task.IsCompleted)
            {
                yield return null;
            }
            if (task.IsFaulted)
            {
                throw task.Exception.GetBaseException();
            }
            onResult(task.Result);
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
        public IEnumerator SetEvaluationContext_Success_FlagsAvailableViaOpenFeature()
        {
            yield return InitFlags("user-123");

            // Internal state assertions
            var internalClient = DdFlags.Instance.GetClient();
            Assert.IsNotNull(internalClient);
            Assert.AreEqual(FlagsClientState.Ready, internalClient.State);

            var ofClient = Api.Instance.GetClient();

            bool boolResult = false;
            yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false), v => boolResult = v);
            Assert.IsTrue(boolResult);

            string stringResult = null;
            yield return AwaitTask(ofClient.GetStringValueAsync("string-flag", "x"), v => stringResult = v);
            Assert.AreEqual("red", stringResult);

            int intResult = 0;
            yield return AwaitTask(ofClient.GetIntegerValueAsync("integer-flag", 0), v => intResult = v);
            Assert.AreEqual(42, intResult);

            double doubleResult = 0.0;
            yield return AwaitTask(ofClient.GetDoubleValueAsync("numeric-flag", 0.0), v => doubleResult = v);
            Assert.AreEqual(3.14, doubleResult, 0.001);

            FlagEvaluationDetails<Value> jsonDetails = null;
            yield return AwaitTask(ofClient.GetObjectDetailsAsync("json-flag", null), v => jsonDetails = v);
            Assert.AreEqual("variation-127", jsonDetails.Variant);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator SetEvaluationContext_ServerError_StateIsError()
        {
            yield return InitFlags(precomputeStatus: 500, precomputeBody: "{\"error\":\"server error\"}");

            var client = DdFlags.Instance.GetClient();
            Assert.IsNotNull(client);
            Assert.AreEqual(FlagsClientState.Error, client.State);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator SetEvaluationContext_ServerErrorAfterCache_StateIsStale()
        {
            // First fetch succeeds
            yield return InitFlags("user-123");
            var client = DdFlags.Instance.GetClient();
            Assert.AreEqual(FlagsClientState.Ready, client.State);

            // Reconfigure mock to return 500
            yield return _mockServer.ConfigureResponse("/precompute-assignments", 500, "{\"error\":\"server error\"}");

            var done = false;
            DdFlags.Instance.GetClient().SetEvaluationContext(new FlagsEvaluationContext("user-456"), _ => done = true);
            yield return new WaitUntil(() => done);

            Assert.AreEqual(FlagsClientState.Stale, client.State);

            // Old flags still evaluable via OpenFeature
            var ofClient = Api.Instance.GetClient();
            string stringResult = null;
            yield return AwaitTask(ofClient.GetStringValueAsync("string-flag", "default"), v => stringResult = v);
            Assert.AreEqual("red", stringResult);
        }

        // ─── Group 3: Exposure telemetry ─────────────────────────────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator BooleanFlagEvaluation_SendsExposureEvent()
        {
            yield return InitFlags("user-123");

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false));

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

            var ofClient = Api.Instance.GetClient();
            for (var i = 0; i < 5; i++)
            {
                yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false));
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
            var ofClient = Api.Instance.GetClient();

            yield return InitFlags("user-A");
            yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false));

            yield return _mockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);
            var done = false;
            DdFlags.Instance.GetClient().SetEvaluationContext(new FlagsEvaluationContext("user-B"), _ => done = true);
            yield return new WaitUntil(() => done);

            yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false));

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

            var ofClient = Api.Instance.GetClient();
            for (var i = 0; i < 3; i++)
            {
                yield return AwaitTask(ofClient.GetStringValueAsync("string-flag", "x"));
            }

            // Flush via internal client (OpenFeature doesn't expose flush)
            DdFlags.Instance.GetClient().Flush();

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

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false));
            yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false));
            yield return AwaitTask(ofClient.GetStringValueAsync("string-flag", "x"));

            DdFlags.Instance.GetClient().Flush();

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

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetStringValueAsync("nonexistent-flag", "default"));

            DdFlags.Instance.GetClient().Flush();

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

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetStringValueAsync("string-flag", "x"));

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

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetStringValueAsync("string-flag", "x"));

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

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetStringValueAsync("string-flag", "x"));
            DdFlags.Instance.GetClient().Flush();

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

        // ─── Group 6: doLog: false suppresses exposure ────────────────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator DoLogFalse_DoesNotSendExposure()
        {
            yield return InitFlags();

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetBooleanValueAsync("no-log-flag", false));

            yield return new WaitForSeconds(2f);

            var exposures = new List<ExposureEventDecoder>();
            yield return _mockServer.PollRequests(TimeSpan.FromSeconds(1), logs =>
            {
                exposures = ExposureEventDecoder.FromMockServer(logs);
                return true;
            });

            Assert.AreEqual(0, exposures.Count, "doLog=false flag must not send an exposure");
        }

        // ─── Group 7: Context attributes flow into exposure payload ──────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator ContextAttributes_AppearedInExposureSubject()
        {
            yield return InitFlags("user-123", new Dictionary<string, object>
            {
                { "plan", "premium" },
                { "age", 30 },
            });

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false));

            var exposures = new List<ExposureEventDecoder>();
            yield return _mockServer.PollRequests(PollTimeout, logs =>
            {
                exposures = ExposureEventDecoder.FromMockServer(logs);
                return exposures.Count >= 1;
            });

            Assert.AreEqual(1, exposures.Count);
            var attrs = exposures[0].SubjectAttributes;
            Assert.IsTrue(attrs.ContainsKey("plan"), "exposure subject should contain 'plan' attribute");
            Assert.AreEqual("premium", attrs["plan"]?.ToString());
            Assert.AreEqual("30", attrs["age"]?.ToString());
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator NestedContextAttributes_FlattenedInPrecomputeRequest()
        {
            yield return InitFlags("user-123", new Dictionary<string, object>
            {
                {
                    "address", new Dictionary<string, object>
                    {
                        { "city", "New York" },
                        { "zip", "10001" },
                    }
                },
            });

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
            var attrs = body["data"]?["attributes"]?["subject"]?["targeting_attributes"];
            Assert.IsNotNull(attrs, "targeting_attributes should be present");
            Assert.AreEqual("New York", (string)attrs["address.city"]);
            Assert.AreEqual("10001", (string)attrs["address.zip"]);
            Assert.IsNull(attrs["address"], "nested key should be absent after flattening");
        }

        // ─── Group 8: StateChanged event ─────────────────────────────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator StateChanged_FiresExpectedTransitionsOnSuccess()
        {
            yield return _mockServer.Clear();
            yield return _mockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            DdFlags.Enable(MakeConfig());
            var client = DdFlags.Instance.CreateClient();
            yield return AwaitTask(Api.Instance.SetProviderAsync(new DatadogFeatureProvider(DdFlags.Instance.GetClient())));

            var transitions = new List<FlagsStateChange>();
            client.StateChanged += (_, change) => transitions.Add(change);

            var done = false;
            var deadline = DateTime.Now + TimeSpan.FromSeconds(20);
            client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), _ => done = true);
            yield return new WaitUntil(() => done || DateTime.Now > deadline);

            Assert.GreaterOrEqual(transitions.Count, 3, "Expected replay + 2 real transitions");

            var replay = transitions[0];
            Assert.AreEqual(FlagsClientState.NotReady, replay.Old);
            Assert.AreEqual(FlagsClientState.NotReady, replay.New, "First event should be a replay (Old == New)");

            var toReconciling = transitions.FirstOrDefault(t => t.New == FlagsClientState.Reconciling);
            Assert.IsNotNull(toReconciling, "Expected NotReady→Reconciling transition");
            Assert.AreEqual(FlagsClientState.NotReady, toReconciling.Old);

            var toReady = transitions.FirstOrDefault(t => t.New == FlagsClientState.Ready);
            Assert.IsNotNull(toReady, "Expected Reconciling→Ready transition");
            Assert.AreEqual(FlagsClientState.Reconciling, toReady.Old);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator StateChanged_FiresErrorTransitionOnServerFailure()
        {
            yield return _mockServer.Clear();
            yield return _mockServer.ConfigureResponse("/precompute-assignments", 500, "{\"error\":\"fail\"}");

            DdFlags.Enable(MakeConfig());
            var client = DdFlags.Instance.CreateClient();
            yield return AwaitTask(Api.Instance.SetProviderAsync(new DatadogFeatureProvider(DdFlags.Instance.GetClient())));

            var transitions = new List<FlagsStateChange>();
            client.StateChanged += (_, change) => transitions.Add(change);

            var done = false;
            client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), _ => done = true);
            yield return new WaitUntil(() => done);

            var toError = transitions.FirstOrDefault(t => t.New == FlagsClientState.Error);
            Assert.IsNotNull(toError, "Expected Reconciling→Error transition on server failure");
            Assert.AreEqual(FlagsClientState.Reconciling, toError.Old);
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator StateChanged_LateSubscriberReceivesCurrentStateReplay()
        {
            yield return InitFlags("user-123");

            var client = DdFlags.Instance.GetClient();
            Assert.AreEqual(FlagsClientState.Ready, client.State);

            FlagsStateChange replayEvent = null;
            client.StateChanged += (_, change) => replayEvent = change;

            Assert.IsNotNull(replayEvent, "Late subscriber should receive an immediate replay");
            Assert.AreEqual(FlagsClientState.Ready, replayEvent.Old);
            Assert.AreEqual(FlagsClientState.Ready, replayEvent.New, "Replay should have Old == New");

            yield return null;
        }

        // ─── Group 9a: Context via OpenFeature API (hybrid workaround) ───────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator SetContextViaOpenFeatureApi_FlagsAvailableAfterEvaluation()
        {
            // Configure mock server but do NOT call InitFlags (which sets context directly on IFlagsClient)
            yield return _mockServer.Clear();
            yield return _mockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            DdFlags.Enable(MakeConfig());
            var ddClient = DdFlags.Instance.CreateClient();
            yield return AwaitTask(Api.Instance.SetProviderAsync(new DatadogFeatureProvider(ddClient)));

            // Set context through the OpenFeature API, NOT via ddClient.SetEvaluationContext
            var ofContext = EvaluationContext.Builder()
                .SetTargetingKey("user-123")
                .Build();
            Api.Instance.SetContext(ofContext);

            // Evaluating a flag should trigger the context fetch via the hybrid workaround
            // and return the correct value from the precomputed payload
            var ofClient = Api.Instance.GetClient();
            bool boolResult = false;
            yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false), v => boolResult = v);

            Assert.IsTrue(boolResult);
        }

        // ─── Group 9: ProviderNotReady error before fetch ─────────────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator EvaluateBeforeFetch_ReturnsProviderNotReadyError()
        {
            yield return _mockServer.Clear();

            DdFlags.Enable(MakeConfig());
            var client = DdFlags.Instance.CreateClient();
            yield return AwaitTask(Api.Instance.SetProviderAsync(new DatadogFeatureProvider(DdFlags.Instance.GetClient())));

            // Evaluate immediately — no SetEvaluationContext has been called yet
            var ofClient = Api.Instance.GetClient();
            FlagEvaluationDetails<bool> details = null;
            yield return AwaitTask(ofClient.GetBooleanDetailsAsync("boolean-flag", false), v => details = v);

            Assert.AreEqual(false, details.Value, "Should return default value");
            Assert.AreEqual(ErrorType.ProviderNotReady, details.ErrorType);

            yield return null;
        }

        // ─── Group 10: TrackExposures / TrackEvaluations = false ─────────────────────

        [UnityTest]
        [Category("integration")]
        public IEnumerator TrackExposuresFalse_DoesNotSendExposureRequest()
        {
            yield return _mockServer.Clear();
            yield return _mockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            DdFlags.Enable(MakeConfig(trackExposures: false));
            var client = DdFlags.Instance.CreateClient();
            yield return AwaitTask(Api.Instance.SetProviderAsync(new DatadogFeatureProvider(DdFlags.Instance.GetClient())));

            var done = false;
            client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), _ => done = true);
            yield return new WaitUntil(() => done);

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetBooleanValueAsync("boolean-flag", false));

            yield return new WaitForSeconds(2f);

            var exposures = new List<ExposureEventDecoder>();
            yield return _mockServer.PollRequests(TimeSpan.FromSeconds(1), logs =>
            {
                exposures = ExposureEventDecoder.FromMockServer(logs);
                return true;
            });

            Assert.AreEqual(0, exposures.Count, "TrackExposures=false should suppress all exposure requests");
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator TrackEvaluationsFalse_DoesNotSendEvaluationRequest()
        {
            yield return _mockServer.Clear();
            yield return _mockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            DdFlags.Enable(MakeConfig(trackEvaluations: false));
            var client = DdFlags.Instance.CreateClient();
            yield return AwaitTask(Api.Instance.SetProviderAsync(new DatadogFeatureProvider(DdFlags.Instance.GetClient())));

            var done = false;
            client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), _ => done = true);
            yield return new WaitUntil(() => done);

            var ofClient = Api.Instance.GetClient();
            yield return AwaitTask(ofClient.GetStringValueAsync("string-flag", "x"));
            DdFlags.Instance.GetClient().Flush();

            yield return new WaitForSeconds(2f);

            var records = new List<EvaluationRecord>();
            yield return _mockServer.PollRequests(TimeSpan.FromSeconds(1), logs =>
            {
                records = EvaluationEventDecoder.AllRecords(logs);
                return true;
            });

            Assert.AreEqual(0, records.Count, "TrackEvaluations=false should suppress all evaluation requests");
        }
    }
}
