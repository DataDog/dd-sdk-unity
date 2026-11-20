// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Datadog.Unity.Flags;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Flags
{
    public class FlagsIntegrationTests : IntegrationTestBase
    {
        private string _precomputePayload;

        [SetUp]
        public void SetUp()
        {
            _precomputePayload = Resources.Load<TextAsset>("PrecomputePayload").text;
        }

        [TearDown]
        public void TearDown()
        {
            DdFlags.Shutdown();
        }

        // ─── Group 1: Precompute request shape ───────────────────────────────────────
        [Test]
        [Category("integration")]
        public async Task PrecomputeRequest_HasCorrectHeaders()
        {
            await InitFlags();

            var received = await MockServer.WaitForRequest("/precompute-assignments", "POST");
            var headers = received.Result.Headers;
            Assert.AreEqual("application/vnd.api+json", headers["Content-Type"]);
            Assert.AreEqual("fake-client-token", headers["dd-client-token"]);
            Assert.AreEqual("fake-rum-application-id", headers["dd-application-id"]);
        }

        [Test]
        [Category("integration")]
        public async Task PrecomputeRequest_BodyHasCorrectJsonApiShape()
        {
            await InitFlags("user-123");

            var received = await MockServer.WaitForRequest("/precompute-assignments", "POST");
            var body = received.Result.JsonBody;
            Assert.AreEqual("precompute-assignments-request", (string)body["data"]["type"]);
            Assert.AreEqual("integration-test", (string)body["data"]["attributes"]["env"]["name"]);
            Assert.AreEqual("user-123", (string)body["data"]["attributes"]["subject"]["targeting_key"]);
            Assert.IsNull(body["data"]["attributes"]["flag_selection"]);
        }

        [Test]
        [Category("integration")]
        public async Task PrecomputeRequest_BodyCarriesCanonicalFlagSelection()
        {
            await InitFlags(
                "user-123",
                flagSelection: new FlagSelection(new[] { " mobile ", "checkout", "mobile" }, new[] { "search.*" }));

            var received = await MockServer.WaitForRequest("/precompute-assignments", "POST");
            var flagSelection = received.Result.JsonBody["data"]["attributes"]["flag_selection"];
            Assert.IsNotNull(flagSelection, "No flag_selection in the precompute body");
            CollectionAssert.AreEqual(new[] { "checkout", "mobile" }, flagSelection["tags"].ToObject<string[]>());
            CollectionAssert.AreEqual(new[] { "search.*" }, flagSelection["key_patterns"].ToObject<string[]>());
        }

        [Test]
        [Category("integration")]
        public async Task PrecomputeRequest_IncludesContextAttributes()
        {
            await InitFlags("user-123", new Dictionary<string, object> { { "plan", "premium" } });

            var received = await MockServer.WaitForRequest("/precompute-assignments", "POST");
            var body = received.Result.JsonBody;
            Assert.AreEqual("premium", (string)body["data"]["attributes"]["subject"]["targeting_attributes"]["plan"]);
        }

        // ─── Group 2: Flags evaluable after fetch ────────────────────────────────────
        [Test]
        [Category("integration")]
        public async Task SetEvaluationContext_Success_FlagsAvailableViaClient()
        {
            await InitFlags("user-123");

            var client = DdFlags.Instance.GetClient();
            Assert.IsNotNull(client);
            Assert.AreEqual(FlagsClientState.Ready, client.State);

            Assert.IsTrue(client.GetBooleanValue("boolean-flag", false));
            Assert.AreEqual("red", client.GetStringValue("string-flag", "x"));
            Assert.AreEqual(42, client.GetIntegerValue("integer-flag", 0));
            Assert.AreEqual(3.14, client.GetDoubleValue("numeric-flag", 0.0), 0.001);

            var jsonDetails = client.GetDetails<object>("json-flag", null);
            Assert.AreEqual("variation-127", jsonDetails.Variant);
        }

        [Test]
        [Category("integration")]
        public async Task SetEvaluationContext_ServerError_StateIsError()
        {
            await InitFlags(precomputeStatus: 500, precomputeBody: "{\"error\":\"server error\"}");

            var client = DdFlags.Instance.GetClient();
            Assert.IsNotNull(client);
            Assert.AreEqual(FlagsClientState.Error, client.State);
        }

        [Test]
        [Category("integration")]
        public async Task SetEvaluationContext_ServerErrorAfterCache_StateIsStale()
        {
            // First fetch succeeds
            await InitFlags("user-123");
            var client = DdFlags.Instance.GetClient();
            Assert.AreEqual(FlagsClientState.Ready, client.State);

            // Reconfigure mock to return 500
            await MockServer.ConfigureResponse("/precompute-assignments", 500, "{\"error\":\"server error\"}");

            var done = false;
            DdFlags.Instance.GetClient().SetEvaluationContext(new FlagsEvaluationContext("user-456"), _ => done = true);
            await AsyncTestHelpers.WaitUntil(() => done);

            Assert.AreEqual(FlagsClientState.Stale, client.State);

            // Old flags still evaluable
            Assert.AreEqual("red", client.GetStringValue("string-flag", "default"));
        }

        [Test]
        [Category("integration")]
        public async Task SetEvaluationContext_ServerErrorAfterEmptyConfiguration_StateIsStale()
        {
            await InitFlags("user-123", precomputeBody: "{\"data\":{\"attributes\":{\"flags\":{}}}}");
            var client = DdFlags.Instance.GetClient();
            Assert.AreEqual(FlagsClientState.Ready, client.State);

            await MockServer.ConfigureResponse("/precompute-assignments", 500, "{\"error\":\"server error\"}");

            var done = false;
            client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), _ => done = true);
            await AsyncTestHelpers.WaitUntil(() => done);

            Assert.AreEqual(FlagsClientState.Stale, client.State);
        }

        // ─── Group 3: Exposure telemetry ─────────────────────────────────────────────
        [Test]
        [Category("integration")]
        public async Task BooleanFlagEvaluation_SendsExposureEvent()
        {
            await InitFlags("user-123");

            DdFlags.Instance.GetClient().GetBooleanValue("boolean-flag", false);

            var received = await MockServer.WaitFor(select: snapshot => snapshot.Exposures, until: values => values.Count >= 1);
            var exposures = received.Result;

            Assert.AreEqual(1, exposures.Count);
            var exp = exposures[0];
            Assert.AreEqual("boolean-flag", exp.FlagKey);
            Assert.AreEqual("allocation-124", exp.AllocationKey);
            Assert.AreEqual("variation-124", exp.VariantKey);
            Assert.AreEqual("user-123", exp.SubjectId);

            var headers = received.Snapshot.RequestsFor("/api/v2/exposures", "POST").First().Headers;
            Assert.AreEqual("fake-client-token", headers["dd-api-key"]);
            Assert.AreEqual("unity", headers["dd-evp-origin"]);
            Assert.IsTrue(headers["Content-Type"].Contains("text/plain"));
        }

        [Test]
        [Category("integration")]
        public async Task SameFlag_EvaluatedMultipleTimes_SendsOnlyOneExposure()
        {
            await InitFlags("user-123");

            var flagsClient = DdFlags.Instance.GetClient();
            for (var i = 0; i < 5; i++)
            {
                flagsClient.GetBooleanValue("boolean-flag", false);
            }

            await AsyncTestHelpers.Delay(TimeSpan.FromSeconds(2));

            var received = await MockServer.WaitFor(select: snapshot => snapshot.Exposures, until: values => values.Count >= 1);
            var exposures = received.Result;

            Assert.AreEqual(1, exposures.Count, "Expected exactly one exposure for the same flag");
        }

        [Test]
        [Category("integration")]
        public async Task ContextChange_SendsFreshExposure()
        {
            // Evaluate for user-A
            await InitFlags("user-A");
            DdFlags.Instance.GetClient().GetBooleanValue("boolean-flag", false);

            // Change context to user-B (requires re-fetch)
            await MockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);
            var done = false;
            DdFlags.Instance.GetClient().SetEvaluationContext(new FlagsEvaluationContext("user-B"), _ => done = true);
            await AsyncTestHelpers.WaitUntil(() => done);

            DdFlags.Instance.GetClient().GetBooleanValue("boolean-flag", false);

            var received = await MockServer.WaitFor(select: snapshot => snapshot.Exposures, until: values => values.Count >= 2);
            var exposures = received.Result;

            Assert.AreEqual(2, exposures.Count, "Expected one exposure per context");
            var subjects = exposures.Select(e => e.SubjectId).ToList();
            CollectionAssert.Contains(subjects, "user-A");
            CollectionAssert.Contains(subjects, "user-B");
        }

        // ─── Group 4: Evaluation telemetry ───────────────────────────────────────────
        [Test]
        [Category("integration")]
        public async Task FlagEvaluation_AfterExplicitFlush_SendsEvaluationBatch()
        {
            await InitFlags("user-123");

            var flagsClient = DdFlags.Instance.GetClient();
            flagsClient.GetStringValue("string-flag", "x");
            flagsClient.GetStringValue("string-flag", "x");
            flagsClient.GetStringValue("string-flag", "x");
            flagsClient.Flush();

            var received = await MockServer.WaitFor(select: snapshot => snapshot.EvaluationBatches, until: values => values.Count >= 1);
            var batches = received.Result;

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

        [Test]
        [Category("integration")]
        public async Task MultipleFlags_ProduceSeparateEvaluationRecords()
        {
            await InitFlags("user-123");

            var flagsClient = DdFlags.Instance.GetClient();
            flagsClient.GetBooleanValue("boolean-flag", false);
            flagsClient.GetBooleanValue("boolean-flag", false);
            flagsClient.GetStringValue("string-flag", "x");
            flagsClient.Flush();

            var received = await MockServer.WaitFor(select: snapshot => snapshot.EvaluationRecords, until: values => values.Count >= 2);
            var records = received.Result;

            Assert.AreEqual(2, records.Count);
            var boolRec = records.FirstOrDefault(r => r.FlagKey == "boolean-flag");
            var strRec = records.FirstOrDefault(r => r.FlagKey == "string-flag");
            Assert.IsNotNull(boolRec);
            Assert.IsNotNull(strRec);
            Assert.AreEqual(2, boolRec.EvaluationCount);
            Assert.AreEqual(1, strRec.EvaluationCount);
        }

        [Test]
        [Category("integration")]
        public async Task MissingFlag_EvaluationRecord_HasRuntimeDefaultUsedTrue()
        {
            await InitFlags("user-123");

            DdFlags.Instance.GetClient().GetStringValue("nonexistent-flag", "default");
            DdFlags.Instance.GetClient().Flush();

            var received = await MockServer.WaitFor(select: snapshot => snapshot.EvaluationRecords, until: values => values.Count >= 1);
            var records = received.Result;

            Assert.AreEqual(1, records.Count);
            var rec = records[0];
            Assert.AreEqual("nonexistent-flag", rec.FlagKey);
            Assert.IsTrue(rec.RuntimeDefaultUsed == true);
            Assert.AreEqual("FLAG_NOT_FOUND", rec.ErrorMessage);
            Assert.IsNull(rec.VariantKey);
            Assert.IsNull(rec.AllocationKey);
        }

        [Test]
        [Category("integration")]
        public async Task Shutdown_FlushesEvaluationsPendingOnTimer()
        {
            await InitFlags("user-123", flushInterval: 60f);

            DdFlags.Instance.GetClient().GetStringValue("string-flag", "x");

            // Shutdown should flush pending evaluations before destroying the aggregator
            DdFlags.Shutdown();

            var received = await MockServer.WaitFor(select: snapshot => snapshot.EvaluationRecords, until: values => values.Count >= 1);
            var records = received.Result;

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("string-flag", records[0].FlagKey);
        }

        [Test]
        [Category("integration")]

        // System.Threading.Timer callbacks do not run in WebGL players.
        [UnityPlatform(exclude = new[] { RuntimePlatform.WebGLPlayer })]
        public async Task TimerFlush_SendsEvaluationBatchAfterInterval()
        {
            await InitFlags("user-123", flushInterval: 1.0f);

            DdFlags.Instance.GetClient().GetStringValue("string-flag", "x");

            // Wait for the timer to fire (interval = 1s, wait a bit longer)
            await AsyncTestHelpers.Delay(TimeSpan.FromSeconds(2.0));

            var received = await MockServer.WaitFor(select: snapshot => snapshot.EvaluationRecords, until: values => values.Count >= 1);
            var records = received.Result;

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual("string-flag", records[0].FlagKey);
        }

        // ─── Group 5: Evaluation EVP headers ─────────────────────────────────────────
        [Test]
        [Category("integration")]
        public async Task EvaluationBatch_HasCorrectEvpHeaders()
        {
            await InitFlags("user-123");

            DdFlags.Instance.GetClient().GetStringValue("string-flag", "x");
            DdFlags.Instance.GetClient().Flush();

            var received = await MockServer.WaitForRequest("/api/v2/flagevaluation", "POST");
            var headers = received.Result.Headers;
            Assert.AreEqual("fake-client-token", headers["dd-api-key"]);
            Assert.AreEqual("unity", headers["dd-evp-origin"]);
            Assert.AreEqual(DatadogSdk.SdkVersion, headers["dd-evp-origin-version"]);
            Assert.IsTrue(headers["Content-Type"].Contains("application/json"));
        }

        // ─── Group 6: doLog: false suppresses exposure ────────────────────────────────
        [Test]
        [Category("integration")]
        public async Task DoLogFalse_DoesNotSendExposure()
        {
            var subjectId = Guid.NewGuid().ToString();
            await InitFlags(subjectId);

            DdFlags.Instance.GetClient().GetBooleanValue("no-log-flag", false);

            await MockServer.ExpectNoneFor(
                snapshot => snapshot.Exposures.Where(value => value.SubjectId == subjectId).ToList(),
                TimeSpan.FromSeconds(2));
        }

        // ─── Group 7: Context attributes flow into exposure payload ──────────────────
        [Test]
        [Category("integration")]
        public async Task ContextAttributes_AppearedInExposureSubject()
        {
            await InitFlags("user-123", new Dictionary<string, object>
            {
                { "plan", "premium" },
                { "age", 30 },
            });

            DdFlags.Instance.GetClient().GetBooleanValue("boolean-flag", false);

            var received = await MockServer.WaitFor(select: snapshot => snapshot.Exposures, until: values => values.Count >= 1);
            var exposures = received.Result;

            Assert.AreEqual(1, exposures.Count);
            var attrs = exposures[0].SubjectAttributes;
            Assert.IsTrue(attrs.ContainsKey("plan"), "exposure subject should contain 'plan' attribute");
            Assert.AreEqual("premium", attrs["plan"]?.ToString());
            Assert.AreEqual("30", attrs["age"]?.ToString());
        }

        [Test]
        [Category("integration")]
        public async Task NestedContextAttributes_FlattenedInPrecomputeRequest()
        {
            await InitFlags("user-123", new Dictionary<string, object>
            {
                {
                    "address", new Dictionary<string, object>
                    {
                        { "city", "New York" },
                        { "zip", "10001" },
                    }
                },
            });

            var received = await MockServer.WaitForRequest("/precompute-assignments", "POST");
            var body = received.Result.JsonBody;
            var attrs = body["data"]?["attributes"]?["subject"]?["targeting_attributes"];
            Assert.IsNotNull(attrs, "targeting_attributes should be present");
            Assert.AreEqual("New York", (string)attrs["address.city"]);
            Assert.AreEqual("10001", (string)attrs["address.zip"]);
            Assert.IsNull(attrs["address"], "nested key should be absent after flattening");
        }

        // ─── Group 8: StateChanged event ─────────────────────────────────────────────
        [Test]
        [Category("integration")]
        public async Task StateChanged_FiresExpectedTransitionsOnSuccess()
        {
            await MockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            DdFlags.Enable(MakeConfig());
            var client = DdFlags.Instance.CreateClient();

            var transitions = new List<FlagsStateChange>();
            client.StateChanged += (_, change) => transitions.Add(change);

            var done = false;
            client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), _ => done = true);
            await AsyncTestHelpers.WaitUntil(() => done);

            // Expect: initial replay (NotReady→NotReady), then NotReady→Reconciling, Reconciling→Ready
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

        [Test]
        [Category("integration")]
        public async Task StateChanged_FiresErrorTransitionOnServerFailure()
        {
            await MockServer.ConfigureResponse("/precompute-assignments", 500, "{\"error\":\"fail\"}");

            DdFlags.Enable(MakeConfig());
            var client = DdFlags.Instance.CreateClient();

            var transitions = new List<FlagsStateChange>();
            client.StateChanged += (_, change) => transitions.Add(change);

            var done = false;
            client.SetEvaluationContext(new FlagsEvaluationContext("user-123"), _ => done = true);
            await AsyncTestHelpers.WaitUntil(() => done);

            var toError = transitions.FirstOrDefault(t => t.New == FlagsClientState.Error);
            Assert.IsNotNull(toError, "Expected Reconciling→Error transition on server failure");
            Assert.AreEqual(FlagsClientState.Reconciling, toError.Old);
        }

        [Test]
        [Category("integration")]
        public async Task StateChanged_LateSubscriberReceivesCurrentStateReplay()
        {
            // Fully initialise first, then subscribe
            await InitFlags("user-123");

            var client = DdFlags.Instance.GetClient();
            Assert.AreEqual(FlagsClientState.Ready, client.State);

            FlagsStateChange replayEvent = null;
            client.StateChanged += (_, change) => replayEvent = change;

            // Replay fires synchronously in the add accessor — no yield needed
            Assert.IsNotNull(replayEvent, "Late subscriber should receive an immediate replay");
            Assert.AreEqual(FlagsClientState.Ready, replayEvent.Old);
            Assert.AreEqual(FlagsClientState.Ready, replayEvent.New, "Replay should have Old == New");

            await Task.Yield();
        }

        // ─── Group 9: ProviderNotReady error before fetch ─────────────────────────────
        [Test]
        [Category("integration")]
        public async Task EvaluateBeforeFetch_ReturnsProviderNotReadyError()
        {
            DdFlags.Enable(MakeConfig());
            var client = DdFlags.Instance.CreateClient();

            // Evaluate immediately — no SetEvaluationContext has been called yet
            var details = client.GetBooleanDetails("boolean-flag", false);

            Assert.AreEqual(false, details.Value, "Should return default value");
            Assert.AreEqual(FlagEvaluationError.ProviderNotReady, details.Error);

            await Task.Yield();
        }

        // ─── Group 10: TrackExposures / TrackEvaluations = false ─────────────────────
        [Test]
        [Category("integration")]
        public async Task TrackExposuresFalse_DoesNotSendExposureRequest()
        {
            var subjectId = Guid.NewGuid().ToString();
            await MockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            DdFlags.Enable(MakeConfig(trackExposures: false));
            var client = DdFlags.Instance.CreateClient();
            var done = false;
            client.SetEvaluationContext(new FlagsEvaluationContext(subjectId), _ => done = true);
            await AsyncTestHelpers.WaitUntil(() => done);

            client.GetBooleanValue("boolean-flag", false);

            await MockServer.ExpectNoneFor(
                snapshot => snapshot.Exposures.Where(value => value.SubjectId == subjectId).ToList(),
                TimeSpan.FromSeconds(2));
        }

        [Test]
        [Category("integration")]
        public async Task TrackEvaluationsFalse_DoesNotSendEvaluationRequest()
        {
            var subjectId = Guid.NewGuid().ToString();
            await MockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            DdFlags.Enable(MakeConfig(trackEvaluations: false));
            var client = DdFlags.Instance.CreateClient();
            var done = false;
            client.SetEvaluationContext(new FlagsEvaluationContext(subjectId), _ => done = true);
            await AsyncTestHelpers.WaitUntil(() => done);

            client.GetStringValue("string-flag", "x");
            client.Flush();

            await MockServer.ExpectNoneFor(
                snapshot => snapshot.EvaluationRecords.Where(value => value.TargetingKey == subjectId).ToList(),
                TimeSpan.FromSeconds(2));
        }

        // ─── Flag selection across clients ───────────────────────────────────────────
        [Test]
        [Category("integration")]
        public async Task FlagSelection_IsSharedByEveryClientAndFixedAtEnable()
        {
            await MockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            var configuredSelection = new FlagSelection(new[] { " mobile", "checkout" });
            DdFlags.Enable(MakeConfig(flagSelection: configuredSelection));
            configuredSelection.Tags[0] = "changed";
            await FetchWithClient("first");
            configuredSelection.Tags[1] = "changed-again";
            await FetchWithClient("second");

            DdFlags.Enable(MakeConfig(flagSelection: new FlagSelection(new[] { "ignored" })));
            await FetchWithClient("third");

            var received = await WaitForFlagSelections(3);
            var selections = received.Result;

            Assert.AreEqual(3, selections.Count);
            foreach (var selection in selections)
            {
                Assert.IsNotNull(selection, "No flag_selection in a precompute body");
                CollectionAssert.AreEqual(new[] { "checkout", "mobile" }, selection["tags"].ToObject<string[]>());
                CollectionAssert.AreEqual(new string[0], selection["key_patterns"].ToObject<string[]>());
            }

            DdFlags.Shutdown();
            await MockServer.Reset();
            await MockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            DdFlags.Enable(MakeConfig(flagSelection: new FlagSelection(keyPatterns: new[] { "search.*" })));
            await FetchWithClient("first");

            received = await WaitForFlagSelections(1);
            selections = received.Result;

            Assert.AreEqual(1, selections.Count);
            CollectionAssert.AreEqual(new string[0], selections[0]["tags"].ToObject<string[]>());
            CollectionAssert.AreEqual(new[] { "search.*" }, selections[0]["key_patterns"].ToObject<string[]>());
        }

        [Test]
        [Category("integration")]
        public async Task DroppedFlagSelection_IsReportedOncePerEnable()
        {
            var tooManyTags = Enumerable.Range(0, 32).Select(i => $"tag-{i}").ToArray();
            var notSent = new Regex(@"flagSelection was not sent \(too_many_values: ");

            await MockServer.ConfigureResponse("/precompute-assignments", 200, _precomputePayload);

            LogAssert.Expect(LogType.Error, notSent);
            DdFlags.Enable(MakeConfig(flagSelection: new FlagSelection(tooManyTags)));
            await FetchWithClient("first");
            await FetchWithClient("second");

            var received = await WaitForFlagSelections(2);
            var selections = received.Result;

            Assert.AreEqual(2, selections.Count);
            Assert.IsTrue(selections.All(s => s == null), "A dropped selection must not be sent");

            DdFlags.Shutdown();
            LogAssert.Expect(LogType.Error, notSent);
            DdFlags.Enable(MakeConfig(flagSelection: new FlagSelection(tooManyTags)));
            await FetchWithClient("first");

            LogAssert.NoUnexpectedReceived();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────────
        private static async Task FetchWithClient(string name)
        {
            var done = false;
            DdFlags.Instance.CreateClient(name)
                .SetEvaluationContext(new FlagsEvaluationContext("user-123"), _ => done = true);
            await AsyncTestHelpers.WaitUntil(() => done);
        }

        private FlagsConfiguration MakeConfig(
            float flushInterval = 60f,
            bool trackExposures = true,
            bool trackEvaluations = true,
            FlagSelection flagSelection = null) => new FlagsConfiguration(
                evaluationFlushIntervalSeconds: flushInterval,
                trackExposures: trackExposures,
                trackEvaluations: trackEvaluations,
                customFlagsEndpoint: $"{MockServer.Endpoint}/precompute-assignments",
                customExposureEndpoint: $"{MockServer.Endpoint}/api/v2/exposures",
                customEvaluationEndpoint: $"{MockServer.Endpoint}/api/v2/flagevaluation",
                flagSelection: flagSelection);

        private async Task InitFlags(
            string targetingKey = "user-123",
            Dictionary<string, object> attributes = null,
            int precomputeStatus = 200,
            string precomputeBody = null,
            float flushInterval = 60f,
            FlagSelection flagSelection = null)
        {
            await MockServer.ConfigureResponse(
                "/precompute-assignments",
                precomputeStatus,
                precomputeBody ?? _precomputePayload);

            DdFlags.Enable(MakeConfig(flushInterval, flagSelection: flagSelection));
            var client = DdFlags.Instance.CreateClient();

            var done = false;
            var context = attributes != null
                ? new FlagsEvaluationContext(targetingKey, attributes)
                : new FlagsEvaluationContext(targetingKey);

            client.SetEvaluationContext(context, _ => done = true);

            await AsyncTestHelpers.WaitUntil(() => done);
        }

        private Task<MockServerResult<List<JToken>>> WaitForFlagSelections(int count) => MockServer.WaitFor(
            select: snapshot => snapshot.RequestsFor("/precompute-assignments", "POST")
                .Select(request => request.JsonBody["data"]["attributes"]["flag_selection"]).ToList(),
            until: selections => selections.Count >= count);
    }
}
