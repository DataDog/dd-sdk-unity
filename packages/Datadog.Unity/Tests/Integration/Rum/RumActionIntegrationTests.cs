// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Linq;
using System.Threading.Tasks;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class RumActionIntegrationTests : IntegrationTestBase
    {
        [Test]
        [Category("integration")]
        public async Task AddAction()
        {
            var viewKey = StartView();

            DatadogSdk.Instance.Rum.AddAction(RumUserActionType.Tap, "Tapped Download", new()
            {
                { "action_id", "download-123" },
                { "action_state", "completed" },
            });

            RumScenario.CloseView(viewKey);
            var received = await MockServer.WaitForClosedView(viewKey, visit => visit.ActionEvents.Count >= 1);
            var action = received.Result.ActionEvents.Single();
            AssertAction(action, "tap", "Tapped Download");
            Assert.AreEqual("download-123", action.Attributes?["action_id"]?.Value<string>());
            Assert.AreEqual("completed", action.Attributes?["action_state"]?.Value<string>());
        }

        [Test]
        [Category("integration")]
        public async Task StartAndStopAction()
        {
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

            RumScenario.CloseView(viewKey);
            var received = await MockServer.WaitForClosedView(viewKey, visit => visit.ActionEvents.Count >= 1);
            var action = received.Result.ActionEvents.Single();
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

        private static void AssertAction(RumActionEventDecoder action, string type, string name)
        {
            Assert.AreEqual(type, action.ActionType);
            Assert.AreEqual(name, action.ActionName);
        }
    }
}
