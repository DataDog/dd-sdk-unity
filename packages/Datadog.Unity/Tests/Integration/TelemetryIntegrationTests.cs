// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration
{
    // Read startup configuration telemetry before other fixtures clear the mock server.
    [NUnit.Framework.Property("Order", -1)]
    public class TelemetryIntegrationTests : IntegrationTestBase
    {
        protected override bool ResetMockServerBeforeTest => false;

        [Test]
        [Category("integration")]
        [Order(0)]
        public async Task ConfigurationTelemetryReportsUnityMetadata()
        {
            DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);

            var received = await MockServer.WaitFor(
                select: snapshot => snapshot.RumEvents.OfType<RumTelemetryEventDecoder>()
                    .Where(value => value.TelemetryType == "configuration")
#if UNITY_WEBGL
                    .Where(value => value.SdkService == "browser-rum-sdk")
#endif
                    .FirstOrDefault(),
                until: value => value != null);
            var configurationEvent = received.Result;

            Assert.AreEqual("unity", configurationEvent.ConfigurationSource);
            Assert.AreEqual(DatadogSdk.SdkVersion, configurationEvent.ConfigurationSdkVersion);
            Assert.AreEqual(DatadogConfigurationOptions.Load().TelemetrySampleRate, configurationEvent.TelemetrySampleRate);
        }

        [Test]
        [Category("integration")]

        // Unity telemetry forwarding is not implemented on WebGL.
        [UnityPlatform(exclude = new[] { RuntimePlatform.WebGLPlayer })]
        public async Task TelemetryIntegrationScenario()
        {
            await MockServer.Reset();

            await AsyncTestHelpers.WaitFor(new MonoBehaviourTest<TestTelemetryMonoBehavior>());

            var expectedMessages = new[]
            {
                "Telemetry Debug Message", "Telemetry Error Message", "Caught bad operation",
                "Telemetry Debug Message With Attributes",
            };
            var received = await MockServer.WaitFor(
                select: snapshot => snapshot.RumEvents.OfType<RumTelemetryEventDecoder>()
                    .Where(value => expectedMessages.Contains(value.Message)).ToList(),
                until: values => expectedMessages.All(message => values.Any(value => value.Message == message)));
            var telemetryEvents = received.Result;

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

            var attributedEvent = telemetryEvents.Single(value => value.Message == expectedMessages[3]);
            Assert.AreEqual(
                "invalid_tag",
                RumEventDecoder.jsonGetProp<string>(attributedEvent.rumEvent, "telemetry.reason"),
                attributedEvent.rumEvent["telemetry"]?.ToString());
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

            internalLogger.TelemetryDebug(
                "Telemetry Debug Message With Attributes",
                new Dictionary<string, object> { { "reason", "invalid_tag" } });

            IsTestFinished = true;
        }
    }
}
