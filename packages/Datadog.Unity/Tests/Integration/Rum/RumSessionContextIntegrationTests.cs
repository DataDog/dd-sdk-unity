// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Linq;
using System.Threading.Tasks;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class RumSessionContextIntegrationTests : IntegrationTestBase
    {
        [Test]
        [Category("integration")]

        // WebGL delegates tracing to the Browser SDK and has no managed session-context provider.
        [UnityPlatform(exclude = new[] { RuntimePlatform.WebGLPlayer })]
        public async Task StartedSessionIsAvailableToManagedCode()
        {
            var sdk = DatadogSdk.Instance;
            sdk.SetTrackingConsent(TrackingConsent.Granted);
            var previousSessionId = sdk.CurrentRumSessionContext?.SessionId;
            var viewKey = $"SessionContext-{Guid.NewGuid():N}";
            var viewOpen = false;
            try
            {
                sdk.Rum.StopSession();
                sdk.Rum.StartView(viewKey, viewKey);
                viewOpen = true;

                RumSessionContext session = null;
                await AsyncTestHelpers.WaitUntil(() =>
                {
                    session = sdk.CurrentRumSessionContext;
                    return session != null && session.SessionId != previousSessionId;
                });
                Assert.IsNotNull(session, "The native RUM context bridge did not return the new session.");

                sdk.Rum.StopView(viewKey);
                viewOpen = false;
                var received = await MockServer.WaitFor(
                    select: snapshot => snapshot.RumEvents.OfType<RumViewEventDecoder>()
                        .FirstOrDefault(value => value.View.Name == viewKey && !value.View.IsActive),
                    until: value => value != null);
                var view = received.Result;
                Assert.AreEqual(
                    view.Session,
                    session.SessionId,
                    "The managed snapshot must match the session emitted by native RUM.");
            }
            finally
            {
                if (viewOpen)
                {
                    sdk.Rum.StopView(viewKey);
                }
            }
        }
    }
}
