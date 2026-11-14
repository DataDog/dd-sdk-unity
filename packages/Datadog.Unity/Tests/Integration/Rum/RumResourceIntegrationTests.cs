// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class RumResourceIntegrationTests : IntegrationTestBase
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator StartAndStopResource()
        {
            var server = new MockServerHelper();
            yield return server.Clear();
            var viewKey = StartView(out var viewStartedAt);
            var rum = DatadogSdk.Instance.Rum;
            var url = $"https://resource.invalid/{viewKey}";
            const string traceId = "1234567890abcdef1234567890abcdef";
            var keys = new[] { $"{viewKey}-first", $"{viewKey}-second" };
            var startedAt = new long[2];
            var clocks = new Stopwatch[2];
            var durationLowerBounds = new double[2];
            var durationUpperBounds = new double[2];

            // Distinct keys sharing a URL must keep their metadata when completed out of order.
            for (var i = 0; i < keys.Length; i++)
            {
                startedAt[i] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                clocks[i] = Stopwatch.StartNew();
                rum.StartResource(keys[i], i == 0 ? RumHttpMethod.Get : RumHttpMethod.Post, url, new()
                {
                    { "resource_index", i },
                    { "request_id", "request-123" },
                    { "resource_state", "loading" },
                    { "_dd.trace_id", traceId },
                    { "_dd.span_id", (456 + i).ToString() },
                    { "_dd.rule_psr", 0.5f },
                });
            }

            // Nanosecond durations must also survive beyond the Int32 limit of about 2.15 seconds.
            yield return new WaitForSecondsRealtime(2.2f);
            foreach (var i in new[] { 1, 0 })
            {
                durationLowerBounds[i] = clocks[i].Elapsed.TotalMilliseconds - 100;
                rum.StopResource(keys[i], RumResourceType.Image, 200 + i, 121999 + i, new()
                {
                    { "completion_reason", "response_received" },
                    { "resource_state", "completed" },
                });
                durationUpperBounds[i] = clocks[i].Elapsed.TotalMilliseconds + 100;
            }

            RumViewVisit visit = null;
            yield return CompleteScenario(server, viewKey, resourceCount: 2, errorCount: 0, value => visit = value);
            Assert.AreEqual(2, visit.ResourceEvents.Count);
            Assert.IsEmpty(visit.ErrorEvents);
            var clockOffset = GetClockOffset(visit, viewStartedAt);

            for (var i = 0; i < keys.Length; i++)
            {
                var resource = visit.ResourceEvents.Single(value => value.Attributes?["resource_index"]?.Value<int>() == i);
                Assert.AreEqual(url, resource.Url);
                Assert.AreEqual(i == 0 ? "GET" : "POST", resource.Method);
                Assert.AreEqual("image", resource.ResourceType);
                Assert.AreEqual(200 + i, resource.StatusCode);
                Assert.AreEqual(121999 + i, resource.Size);
                Assert.That(resource.Date, Is.EqualTo(startedAt[i] + clockOffset).Within(100));
                Assert.Greater(resource.Duration, (long)int.MaxValue);
                Assert.That(
                    resource.Duration / 1000000.0,
                    Is.InRange(durationLowerBounds[i], durationUpperBounds[i]),
                    "Resource duration is in nanoseconds");
                AssertAttributes(resource, "completed", "response_received");
                // TODO: Follow up with Browser SDK and remove this gate when manual trace attributes are promoted.
                // Unity's previous WebGL implementation also used the incorrect `dd` key instead of `_dd`.
#if !UNITY_WEBGL
                Assert.AreEqual(traceId, resource.TraceId);
                Assert.AreEqual((456 + i).ToString(), resource.SpanId);
                Assert.AreEqual(0.5, resource.rumEvent["_dd"]?["rule_psr"]?.Value<double>());
                Assert.IsNull(resource.Attributes["_dd.trace_id"], "Trace metadata belongs in _dd, not context");
                Assert.IsNull(resource.Attributes["_dd.span_id"]);
                Assert.IsNull(resource.Attributes["_dd.rule_psr"]);
#endif
            }
        }

        [UnityTest]
        [Category("integration")]
        public IEnumerator StopResourceWithError()
        {
            var server = new MockServerHelper();
            yield return server.Clear();
            var viewKey = StartView(out var viewStartedAt);
            var rum = DatadogSdk.Instance.Rum;
            var key = $"{viewKey}-failed";
            var url = $"https://resource.invalid/{viewKey}";
            rum.StartResource(key, RumHttpMethod.Post, url, new()
            {
                { "request_id", "request-123" },
                { "resource_state", "loading" },
            });
            // Keep request start outside the failure timestamp tolerance.
            yield return new WaitForSecondsRealtime(0.25f);

            var stoppedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            rum.StopResourceWithError(key, "ResourceLoadError", "Resource request failed", new()
            {
                { "completion_reason", "network_error" },
                { "resource_state", "failed" },
            });

            RumViewVisit visit = null;
#if UNITY_WEBGL
            const int resourceCount = 1;
#else
            const int resourceCount = 0;
#endif
            yield return CompleteScenario(server, viewKey, resourceCount, errorCount: 1, value => visit = value);
            Assert.AreEqual(1, visit.ErrorEvents.Count);
            Assert.AreEqual(resourceCount, visit.ResourceEvents.Count);
#if UNITY_WEBGL
            var resource = visit.ResourceEvents.Single();
            Assert.AreEqual(url, resource.Url);
            Assert.AreEqual("POST", resource.Method);
            Assert.AreEqual("other", resource.ResourceType);
            AssertAttributes(resource, "failed", "network_error");
#endif

            var error = visit.ErrorEvents.Single();
            Assert.AreEqual(url, error.ResourceUrl);
            Assert.AreEqual("POST", error.ResourceMethod);
            Assert.AreEqual("network", error.Source);
            Assert.AreEqual("ResourceLoadError", error.ErrorType);
            Assert.AreEqual("Resource request failed", error.Message);
            // TODO(RUM-19988): Remove this gate when the Android SDK pin includes
            // https://github.com/DataDog/dd-sdk-android/pull/4151.
#if !UNITY_ANDROID
            var clockOffset = GetClockOffset(visit, viewStartedAt);
            Assert.That(error.Date, Is.EqualTo(stoppedAt + clockOffset).Within(100));
#endif
            AssertAttributes(error, "failed", "network_error");
        }

        private static string StartView(out long startedAt)
        {
            var sdk = DatadogSdk.Instance;
            sdk.SetTrackingConsent(TrackingConsent.Granted);
            Assert.IsNotNull(sdk.Rum, "RUM must be initialized before running resource scenarios");
            var key = $"ManualResource-{Guid.NewGuid():N}";
            startedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            sdk.Rum.StartView(key, key);
            return key;
        }

        private static IEnumerator CompleteScenario(
            MockServerHelper server,
            string viewKey,
            int resourceCount,
            int errorCount,
            Action<RumViewVisit> receive)
        {
#if UNITY_WEBGL
            // Browser views close when a successor starts; StopView is a no-op.
            DatadogSdk.Instance.Rum.StartView($"{viewKey}-complete");
#else
            DatadogSdk.Instance.Rum.StopView(viewKey);
#endif
            RumViewVisit visit = null;
            yield return server.PollRequests(TimeSpan.FromSeconds(30), logs =>
            {
                visit = RumDecoderHelpers.RumSessionsFromEvents(RumDecoderHelpers.RumEventsFromMockServer(logs))
                    .SelectMany(session => session.Visits).SingleOrDefault(value => value.Name == viewKey);
                return visit != null && visit.ViewEvents.Any(value => !value.View.IsActive)
                    && visit.ResourceEvents.Count >= resourceCount && visit.ErrorEvents.Count >= errorCount;
            });
            Assert.IsNotNull(visit, $"No RUM view received for {viewKey}");
            var closedView = visit.ViewEvents.LastOrDefault(value => !value.View.IsActive);
            Assert.IsNotNull(closedView, $"The RUM view {viewKey} did not close");
            receive(visit);
        }

        private static long GetClockOffset(RumViewVisit visit, long viewStartedAt)
        {
            // Native SDKs cache a clock offset for the view. Estimate it from the SDK's
            // view date and our local StartView timestamp, then reuse it for events in that view.
            return visit.ViewEvents.First().Date - viewStartedAt;
        }

        private static void AssertAttributes(RumEventDecoder value, string expectedState, string expectedReason)
        {
            Assert.IsNotNull(value.Attributes);
            Assert.AreEqual("request-123", value.Attributes["request_id"]?.Value<string>());
            Assert.AreEqual(expectedReason, value.Attributes["completion_reason"]?.Value<string>());
            Assert.AreEqual(expectedState, value.Attributes["resource_state"]?.Value<string>(), "Stop attributes override start attributes");
        }
    }
}
