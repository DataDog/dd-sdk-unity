// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration
{
    public class TelemetryIntegrationTests : IntegrationTestBase
    {
        [UnityTest]
        [Category("integration")]
        // Unity telemetry forwarding is not implemented on WebGL.
        [UnityPlatform(exclude = new[] { RuntimePlatform.WebGLPlayer })]
        public IEnumerator TelemetryIntegrationScenario()
        {
            var mockServerHelper = new MockServerHelper();
            yield return mockServerHelper.Clear();

            yield return new MonoBehaviourTest<TestTelemetryMonoBehavior>();

            var expectedMessages = new[] { "Telemetry Debug Message", "Telemetry Error Message", "Caught bad operation" };
            List<MockServerLog> serverLog = new();
            yield return mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (log) =>
            {
                serverLog = log;
                var events = RumDecoderHelpers.RumEventsFromMockServer(serverLog);
                // Session renewals can upload unrelated native telemetry after the server is reset.
                var telemetryEvents = events.OfType<RumTelemetryEventDecoder>().ToList();
                return expectedMessages.All(message => telemetryEvents.Any(value => value.Message == message));
            });

            var telemetryEvents = RumDecoderHelpers.RumEventsFromMockServer(serverLog)
                .OfType<RumTelemetryEventDecoder>()
                .Where(value => expectedMessages.Contains(value.Message))
                .ToList();

            Assert.AreEqual(expectedMessages.Length, telemetryEvents.Count);
            // Native clock adjustments can change timestamp order between consecutive events.
            CollectionAssert.AreEquivalent(expectedMessages, telemetryEvents.Select(value => value.Message));

            var errorEvent = telemetryEvents.Single(value => value.Message == expectedMessages[1]);
            Assert.IsTrue(errorEvent.ErrorStack == null || errorEvent.ErrorStack == "unknown");
            Assert.IsTrue(errorEvent.ErrorKind == null || errorEvent.ErrorKind == "unknown");

            var exceptionEvent = telemetryEvents.Single(value => value.Message == expectedMessages[2]);
            StringAssert.Contains("System.InvalidOperationException: Bad operation", exceptionEvent.ErrorStack);
            StringAssert.Contains("TelemetryTest", exceptionEvent.ErrorStack);
            Assert.AreEqual("System.InvalidOperationException", exceptionEvent.ErrorKind);
        }
    }

    public class TestTelemetryMonoBehavior : MonoBehaviour, IMonoBehaviourTest
    {
        public bool IsTestFinished { get; private set; }

        public void Awake()
        {
            IsTestFinished = false;
            DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);
            TelemetryTest();
        }

        private void TelemetryTest()
        {
            DatadogSdk.Instance.Rum.StartView("StartView");

            var internalLogger = DatadogSdk.Instance.InternalLogger;
            internalLogger.TelemetryDebug("Telemetry Debug Message");
            internalLogger.TelemetryError("Telemetry Error Message", null);

            try
            {
                throw new InvalidOperationException("Bad operation");
            }
            catch (InvalidOperationException e)
            {
                internalLogger.TelemetryError("Caught bad operation", e);
            }

            IsTestFinished = true;
        }
    }
}
