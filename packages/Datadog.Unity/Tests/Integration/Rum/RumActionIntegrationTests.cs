// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Linq;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class RumActionIntegrationTests : IntegrationTestBase
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator AddAction()
        {
            var server = new MockServerHelper();
            yield return server.Clear();
            var viewKey = StartView();

            DatadogSdk.Instance.Rum.AddAction(RumUserActionType.Tap, "Tapped Download", new()
            {
                { "action_id", "download-123" },
                { "action_state", "completed" },
            });

            RumViewVisit visit = null;
            yield return CompleteScenario(server, viewKey, value => visit = value);
            var action = visit.ActionEvents.Single();
            AssertAction(action, "tap", "Tapped Download");
            Assert.AreEqual("download-123", action.Attributes?["action_id"]?.Value<string>());
            Assert.AreEqual("completed", action.Attributes?["action_state"]?.Value<string>());
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator StartAndStopAction()
        {
            var server = new MockServerHelper();
            yield return server.Clear();
            var viewKey = StartView();
            var rum = DatadogSdk.Instance.Rum;

            rum.StartAction(RumUserActionType.Scroll, "Scrolled Gallery", new()
            {
                { "action_id", "gallery-123" },
                { "action_state", "scrolling" },
            });

            rum.StopAction(RumUserActionType.Scroll, "Scrolled Gallery", new()
            {
                { "completion_reason", "gesture_ended" },
                { "action_state", "completed" },
            });

            RumViewVisit visit = null;
            yield return CompleteScenario(server, viewKey, value => visit = value);
            var action = visit.ActionEvents.Single();
            AssertAction(action, "scroll", "Scrolled Gallery");
            Assert.AreEqual("gallery-123", action.Attributes?["action_id"]?.Value<string>());
            Assert.AreEqual("gesture_ended", action.Attributes?["completion_reason"]?.Value<string>());
            Assert.AreEqual(
                "completed",
                action.Attributes?["action_state"]?.Value<string>(),
                "Stop attributes override start attributes");
        }

        private static string StartView()
        {
            var sdk = DatadogSdk.Instance;
            sdk.SetTrackingConsent(TrackingConsent.Granted);
            Assert.IsNotNull(sdk.Rum, "RUM must be initialized before running action scenarios");
            var key = $"ManualAction-{Guid.NewGuid():N}";
            sdk.Rum.StartView(key, key);
            return key;
        }

        private static IEnumerator CompleteScenario(MockServerHelper server, string viewKey, Action<RumViewVisit> receive)
        {
#if UNITY_WEBGL
            // Browser views close when a successor starts; StopView is a no-op.
            // The test-runner prefix excludes delayed cleanup-view updates from later scenarios.
            DatadogSdk.Instance.Rum.StartView($"InitTestScene-{viewKey}-complete");
#else
            DatadogSdk.Instance.Rum.StopView(viewKey);
#endif
            RumViewVisit visit = null;
            yield return server.PollRequests(TimeSpan.FromSeconds(30), logs =>
            {
                visit = RumDecoderHelpers.RumSessionsFromEvents(RumDecoderHelpers.RumEventsFromMockServer(logs))
                    .SelectMany(session => session.Visits).SingleOrDefault(value => value.Name == viewKey);
                return visit != null && visit.ViewEvents.Any(value => !value.View.IsActive)
                    && visit.ActionEvents.Count >= 1;
            });
            Assert.IsNotNull(visit, $"No RUM view received for {viewKey}");
            Assert.IsTrue(visit.ViewEvents.Any(value => !value.View.IsActive), $"The RUM view {viewKey} did not close");
            Assert.AreEqual(1, visit.ActionEvents.Count);
            receive(visit);
        }

        private static void AssertAction(RumActionEventDecoder action, string type, string name)
        {
            Assert.AreEqual(type, action.ActionType);
            Assert.AreEqual(name, action.ActionName);
        }
    }
}
