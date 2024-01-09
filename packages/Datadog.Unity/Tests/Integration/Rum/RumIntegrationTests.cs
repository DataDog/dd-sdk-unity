// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class RumIntegrationTests
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator RumIntegrationScenario()
        {
            var mockServerHelper = new MockServerHelper();
            var resetTask = mockServerHelper.Clear();
            yield return new WaitUntil(() => resetTask.IsCompleted);

            yield return new MonoBehaviourTest<TestRumMonoBehavior>();
            var task = mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (serverLog) =>
            {
                var events = RumDecoderHelpers.RumEventsFromMockServer(serverLog);
                var sessions = RumDecoderHelpers.RumSessionsFromEvents(events);
                // Second view makes sure the first one has been closed
                return sessions.Count >= 1 && sessions[0].Visits.Count >= 5;
            });

            yield return new WaitUntil(() => task.IsCompleted);
            var serverLog = task.Result;
            foreach (var log in serverLog)
            {
                VerifyCommonTags(log);
            }

            var sessions = RumDecoderHelpers.RumSessionsFromEvents(
                RumDecoderHelpers.RumEventsFromMockServer(serverLog));

            Assert.AreEqual(1, sessions.Count);

            var session = sessions.First();

            // Discard visits that are automatically recorded parts of integration testing
            var visits = session.Visits.Where(
                visit => visit.Name != string.Empty && !visit.Name.Contains("InitTestScene")).ToArray();
            Assert.AreEqual(3, visits.Length);

            var firstVisit = visits[0];
            Assert.AreEqual("First Screen", firstVisit.Name);
            Assert.AreEqual(1, firstVisit.ViewEvents.First().Attributes["onboarding_stage"].Value<int>());

            Assert.AreEqual(1, firstVisit.ActionEvents.Count);
            var firstAction = firstVisit.ActionEvents[0];
            Assert.AreEqual("tap", firstAction.ActionType);
            Assert.AreEqual("Tapped Download", firstAction.ActionName);
            Assert.AreEqual(1, firstAction.Attributes["onboarding_stage"].Value<int>());

            Assert.AreEqual(1, firstVisit.ResourceEvents.Count);
            var firstResource = firstVisit.ResourceEvents[0];
            Assert.AreEqual("http://fake/resource/1", firstResource.Url);
            Assert.AreEqual("GET", firstResource.Method);
            Assert.AreEqual("image", firstResource.ResourceType);
            Assert.AreEqual(200, firstResource.StatusCode);
            Assert.AreEqual(121999, firstResource.Size);
            Assert.GreaterOrEqual(firstResource.Duration, 50 * 1000 * 1000);

            Assert.AreEqual(1, firstVisit.ErrorEvents.Count);
            var resourceError = firstVisit.ErrorEvents[0];
            Assert.AreEqual("http://fake/resource/2", resourceError.ResourceUrl);
            Assert.AreEqual("POST", resourceError.ResourceMethod);
            Assert.AreEqual("System.Net.NetworkInformation.NetworkInformationException", resourceError.ErrorType);
            Assert.AreEqual("network", resourceError.Source);

            var secondVisit = visits[1];
            Assert.AreEqual(1, secondVisit.ErrorEvents.Count);
            var errorEvent = secondVisit.ErrorEvents[0];

            // Android resources don't have ErrorType
#if !UNITY_ANDROID
            Assert.AreEqual("System.Exception", errorEvent.ErrorType);
#endif

            Assert.AreEqual("Test Exception", errorEvent.Message);
            Assert.IsNotNull(errorEvent.Stack);
            Assert.AreEqual("source", errorEvent.Source);
            Assert.AreEqual("first_call", errorEvent.Attributes["error_attribute"].Value<string>());
            Assert.AreEqual(1, errorEvent.Attributes["onboarding_stage"].Value<int>());

            Assert.AreEqual(1, secondVisit.ActionEvents.Count);
            var secondAction = secondVisit.ActionEvents[0];
            Assert.AreEqual("tap", secondAction.ActionType);
            Assert.AreEqual("Tapped Exception", secondAction.ActionName);

            var finalSecondVisitView = secondVisit.ViewEvents.Last();
            Assert.AreEqual("True", finalSecondVisitView.FeatureFlags["mock_flag_a"].Value<string>());
            Assert.AreEqual("mock_value", finalSecondVisitView.FeatureFlags["mock_flag_b"].Value<string>());

            var automaticVisit = visits[2];
            var visitView = automaticVisit.ViewEvents.Last();
            Assert.AreEqual(visitView.View.Name, "EmptyScene");
            Assert.IsNotNull(visitView.Attributes["is_sub_scene"].Value<bool>());
            Assert.AreEqual(true, visitView.Attributes["is_loaded"].Value<bool>());
        }

        private void VerifyCommonTags(MockServerLog log)
        {
            foreach (var request in log.Requests)
            {
                Assert.AreEqual("unity", request.QueryParameters["ddsource"]);
            }
        }
    }

    public class TestRumMonoBehavior : MonoBehaviour, IMonoBehaviourTest
    {
        public bool IsTestFinished { get; private set; }

        public void Awake()
        {
            IsTestFinished = false;
            DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);

            StartCoroutine(RumTest());
        }

        public IEnumerator RumTest()
        {
            var rum = DatadogSdk.Instance.Rum;
            rum?.AddAttribute("onboarding_stage", 1);
            rum?.StartView("FirstScreen", name: "First Screen");

            yield return new WaitForSeconds(0.05f);
            rum?.AddAction(RumUserActionType.Tap, "Tapped Download");

            var resourceKey1 = "/resource/1";
            var resourceKey2 = "/resource/2";
            rum?.StartResource(resourceKey1, RumHttpMethod.Get, $"http://fake{resourceKey1}");
            rum?.StartResource(resourceKey2, RumHttpMethod.Post, $"http://fake{resourceKey2}");

            yield return new WaitForSeconds(0.05f);
            rum?.StopResource(resourceKey1, RumResourceType.Image, 200, 121999);

            yield return new WaitForSeconds(0.03f);
            rum?.StopResource(resourceKey2, new NetworkInformationException());

            rum?.StartView("ErrorScreen", name: "Error Screen");
            rum?.AddFeatureFlagEvaluation("mock_flag_a", true);
            rum?.AddFeatureFlagEvaluation("mock_flag_b", "mock_value");

            rum?.AddAction(RumUserActionType.Tap, "Tapped Exception");

            try
            {
                throw new Exception("Test Exception");
            }
            catch(Exception e)
            {
                rum?.AddError(e, RumErrorSource.Source, new()
                {
                    { "error_attribute", "first_call" },
                });
            }

            SceneManager.LoadScene("Scenes/EmptyScene");

            IsTestFinished = true;
        }
    }
}
