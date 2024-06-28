// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Linq;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration
{
    public class TelemetryIntegrationTests
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator TelemetryIntegrationScenario()
        {
            var mockServerHelper = new MockServerHelper();
            var resetTask = mockServerHelper.Clear();
            yield return new WaitUntil(() => resetTask.IsCompleted);

            yield return new MonoBehaviourTest<TestTelemetryMonoBehavior>();
            var task = mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), (serverLog) =>
            {
                var events = RumDecoderHelpers.RumEventsFromMockServer(serverLog);
                var telemetyEvents = events
                    .Where(x => x is RumTelemetryEventDecoder telem &&
                                telem.TelemetryType != "configuration");
                return telemetyEvents.Count() >= 3;
            });

            yield return new WaitUntil(() => task.IsCompleted);
            var serverLog = task.Result;
            var telemetryEvents = RumDecoderHelpers.RumEventsFromMockServer(serverLog)
                .Where(x => x is RumTelemetryEventDecoder telem && telem.TelemetryType != "configuration")
                .ToList();

            Assert.AreEqual(3, telemetryEvents.Count);

            var debugEvent = (RumTelemetryEventDecoder)telemetryEvents[0];
            Assert.AreEqual("Telemetry Debug Message", debugEvent.Message);

            var errorEvent = (RumTelemetryEventDecoder)telemetryEvents[1];
            Assert.AreEqual("Telemetry Error Message", errorEvent.Message);
            Assert.IsTrue(errorEvent.ErrorStack == null || errorEvent.ErrorStack == "unknown");
            Assert.IsTrue(errorEvent.ErrorKind == null || errorEvent.ErrorKind == "unknown");

            var exceptionEvent = (RumTelemetryEventDecoder)telemetryEvents[2];
            Assert.AreEqual("Caught bad operation", exceptionEvent.Message);
            Assert.IsNotNull(exceptionEvent.ErrorStack);
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
