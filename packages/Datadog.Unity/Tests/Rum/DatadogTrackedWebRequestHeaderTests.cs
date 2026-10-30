// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.Text;
using NUnit.Framework;
using UnityEngine.Networking;

namespace Datadog.Unity.Rum.Tests
{
    public class DatadogTrackedWebRequestHeaderTests
    {
        [Test]
        public void CapturesListedRequestHeadersSetBeforeAndAfterWrapping()
        {
            // Given
            var filter = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, new[] { "x-before", "x-after" });
            var inner = new UnityWebRequest("https://example.com/", "GET");
            inner.SetRequestHeader("X-Before", "1");
            using var request = new DatadogTrackedWebRequest(inner);
            request.SetRequestHeader("X-After", "2");
            request.SetRequestHeader("X-Not-Listed", "3");

            // When
            var headers = request.CaptureRequestHeaders(filter, null);
            var disabledHeaders = request.CaptureRequestHeaders(new ResourceHeaderFilter(TrackResourceHeadersMode.Disabled), null);

            // Then
            CollectionAssert.AreEquivalent(new[] { "x-before", "x-after" }, headers.Keys);
            Assert.AreEqual("1", headers["x-before"]);
            Assert.AreEqual("2", headers["x-after"]);
            Assert.IsNull(disabledHeaders);
        }

        [Test]
        public void FallsBackToTheUploadHandlerContentType()
        {
            // Given
            var filter = new ResourceHeaderFilter(TrackResourceHeadersMode.Defaults);
            var inner = new UnityWebRequest("https://example.com/", "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}")) { contentType = "application/json" },
            };
            using var request = new DatadogTrackedWebRequest(inner);

            // When
            var headers = request.CaptureRequestHeaders(filter, null);

            // Then
            Assert.AreEqual("application/json", headers["content-type"]);
        }
    }
}
