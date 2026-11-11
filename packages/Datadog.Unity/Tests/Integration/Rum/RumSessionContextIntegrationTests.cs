// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections;
using System.Linq;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class RumSessionContextIntegrationTests : IntegrationTestBase
    {
        [UnityTest]
        [Category("integration")]
        // WebGL delegates tracing to the Browser SDK and has no managed session-context provider.
        [UnityPlatform(exclude = new[] { RuntimePlatform.WebGLPlayer })]
        public IEnumerator StartedSessionIsAvailableToManagedCode()
        {
            var server = new MockServerHelper();
            yield return server.Clear();

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
                var deadline = Time.realtimeSinceStartup + 20;
                while (Time.realtimeSinceStartup < deadline)
                {
                    var current = sdk.CurrentRumSessionContext;
                    if (current != null && current.SessionId != previousSessionId)
                    {
                        session = current;
                        break;
                    }
                    yield return null;
                }
                Assert.IsNotNull(session, "The native RUM context bridge did not return the new session.");

                sdk.Rum.StopView(viewKey);
                viewOpen = false;
                RumViewEventDecoder view = null;
                yield return server.PollRequests(TimeSpan.FromSeconds(30), logs =>
                {
                    view = RumDecoderHelpers.RumEventsFromMockServer(logs).OfType<RumViewEventDecoder>()
                        .FirstOrDefault(value => value.View.Name == viewKey && !value.View.IsActive);
                    return view != null;
                });
                Assert.IsNotNull(view, $"No closed native RUM view received for {viewKey}.");
                Assert.AreEqual(view.Session, session.SessionId,
                    "The managed snapshot must match the session emitted by native RUM.");
            }
            finally
            {
                if (viewOpen) sdk.Rum.StopView(viewKey);
            }
        }
    }
}
