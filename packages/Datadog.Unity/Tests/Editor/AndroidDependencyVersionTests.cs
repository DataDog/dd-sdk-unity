// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using Datadog.Unity.Editor.Android;
using NUnit.Framework;

namespace Datadog.Unity.Editor.Tests
{
    public class AndroidDependencyVersionTests
    {
        [Test]
        public void ParsesValidJson()
        {
            string json = @"{
  ""version"": ""3.10.0"",
  ""artifacts"": [
    ""dd-sdk-android-rum"",
    ""dd-sdk-android-logs"",
    ""dd-sdk-android-ndk""
  ]
}";
            AndroidDependencyPinData data = AndroidDependencyVersion.Parse(json);
            Assert.AreEqual("3.10.0", data.version);
            Assert.AreEqual(
                new[] { "dd-sdk-android-rum", "dd-sdk-android-logs", "dd-sdk-android-ndk" },
                data.artifacts);
        }

        [Test]
        public void ThrowsWhenVersionIsMissing()
        {
            string json = @"{
  ""artifacts"": [
    ""dd-sdk-android-rum""
  ]
}";
            Assert.Throws<InvalidOperationException>(() => AndroidDependencyVersion.Parse(json));
        }

        [Test]
        public void ThrowsWhenVersionIsEmpty()
        {
            string json = @"{
  ""version"": """",
  ""artifacts"": [
    ""dd-sdk-android-rum""
  ]
}";
            Assert.Throws<InvalidOperationException>(() => AndroidDependencyVersion.Parse(json));
        }

        [Test]
        public void ThrowsWhenArtifactsIsMissing()
        {
            string json = @"{
  ""version"": ""3.10.0""
}";
            Assert.Throws<InvalidOperationException>(() => AndroidDependencyVersion.Parse(json));
        }

        [Test]
        public void ThrowsWhenArtifactsIsEmpty()
        {
            string json = @"{
  ""version"": ""3.10.0"",
  ""artifacts"": []
}";
            Assert.Throws<InvalidOperationException>(() => AndroidDependencyVersion.Parse(json));
        }
    }
}
