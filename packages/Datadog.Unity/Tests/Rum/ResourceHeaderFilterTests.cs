// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using NSubstitute;
using NUnit.Framework;

namespace Datadog.Unity.Rum.Tests
{
    public class ResourceHeaderFilterTests
    {
        [Test]
        public void CapturesOnlyListedHeadersAndLowercasesKeys()
        {
            // Given
            var filter = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, new[] { "X-Request-Id", "etag" });

            // When
            var result = filter.FilterResponseHeaders(new Dictionary<string, string>
            {
                { "X-REQUEST-ID", "abc" },
                { "ETag", "\"1\"" },
                { "Content-Type", "text/plain" },
            }, null);

            // Then
            CollectionAssert.AreEquivalent(new Dictionary<string, object>
            {
                { "x-request-id", "abc" },
                { "etag", "\"1\"" },
            }, result);
        }

        [Test]
        public void NeverCapturesSensitiveHeadersEvenWhenListed()
        {
            // Given
            var sensitive = new[] { "authorization", "cookie", "x-api-key", "x-auth-token", "x-forwarded-for", "cf-connecting-ip" };
            var filter = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, sensitive.Append("x-safe"));
            var headers = sensitive.Append("x-safe").ToDictionary(name => name, _ => "value");

            // When
            var result = filter.FilterResponseHeaders(headers, null);

            // Then
            CollectionAssert.AreEquivalent(new[] { "x-safe" }, result.Keys);
        }

        [Test]
        public void TruncatesValuesTo128BytesWithoutSplittingCharacters()
        {
            // Given
            // 'é' is 2 bytes in UTF-8 and the emoji is a 4-byte surrogate pair
            var filter = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, new[] { "x-accented", "x-emoji" });

            // When
            var result = filter.FilterResponseHeaders(new Dictionary<string, string>
            {
                { "x-accented", new string('é', 100) },
                { "x-emoji", new string('a', 127) + "😀" },
            }, null);

            // Then
            Assert.AreEqual(new string('é', 64), result["x-accented"]);
            Assert.AreEqual(new string('a', 127), result["x-emoji"]);
        }

        [Test]
        public void CapturesAtMost100Headers()
        {
            // Given
            var names = Enumerable.Range(0, 150).Select(i => $"x-h{i}").ToList();
            var filter = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, names);

            // When
            var result = filter.FilterResponseHeaders(names.ToDictionary(name => name, _ => "v"), null);

            // Then
            Assert.AreEqual(100, result.Count);
        }

        [Test]
        public void SkipsHeadersOverTheSizeLimitAndLogsAWarning()
        {
            // Given
            // Each big header takes 128 bytes of value: 15 of them fit in 2048 bytes, the 16th does not
            var bigNames = Enumerable.Range(0, 16).Select(i => $"x-big-{i:D2}").ToList();
            var filter = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, bigNames.Append("x-small"));
            var headers = bigNames.ToDictionary(name => name, _ => new string('v', 128));
            headers["x-small"] = "s";
            var logger = Substitute.For<IInternalLogger>();

            // When
            var result = filter.FilterResponseHeaders(headers, logger);

            // Then
            Assert.IsFalse(result.ContainsKey("x-big-15"));
            Assert.AreEqual("s", result["x-small"]);
            Assert.LessOrEqual(result.Sum(h => Encoding.UTF8.GetByteCount(h.Key) + Encoding.UTF8.GetByteCount((string)h.Value)), 2048);
            logger.Received(1).Log(DdLogLevel.Warn, Arg.Is<string>(message => message.Contains("x-big-15")));
        }

        [Test]
        public void ConvertsTheConfigurationToTheBrowserSdkOption()
        {
            // Given
            var custom = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, new[] { "X-Request-Id", "authorization" });
            var customWithDefaults = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, new[] { "x-request-id" }, includeDefaultHeaders: true);
            var defaults = new ResourceHeaderFilter(TrackResourceHeadersMode.Defaults);
            var disabled = new ResourceHeaderFilter(TrackResourceHeadersMode.Disabled, new[] { "x-request-id" });

            // When
            var customOption = (List<Dictionary<string, object>>)custom.ToBrowserSdkOption();
            var customWithDefaultsOption = (List<Dictionary<string, object>>)customWithDefaults.ToBrowserSdkOption();
            var defaultsOption = defaults.ToBrowserSdkOption();
            var disabledOption = disabled.ToBrowserSdkOption();

            // Then
            Assert.AreEqual(1, customOption.Count);
            Assert.AreEqual("x-request-id", customOption[0]["name"]);
            // The Browser SDK cannot combine its defaults with custom matchers, so the defaults are listed by name
            CollectionAssert.IsSupersetOf(customWithDefaultsOption.Select(matcher => matcher["name"]), new[] { "cache-control", "cf-cache-status", "x-request-id" });
            Assert.AreEqual(true, defaultsOption);
            Assert.IsNull(disabledOption);
        }

        [Test]
        public void DefaultsCaptureTheDefaultRequestAndResponseHeaders()
        {
            // Given
            var filter = new ResourceHeaderFilter(TrackResourceHeadersMode.Defaults);
            var defaultResponseHeaders = new[]
            {
                "cache-control", "etag", "age", "expires", "content-type", "content-encoding", "content-length", "vary",
                "server-timing", "x-cache", "cf-cache-status", "x-vercel-cache",
            };
            var headers = defaultResponseHeaders.Append("x-request-id").ToDictionary(name => name, _ => "v");

            // When
            var request = filter.FilterRequestHeaders(name => headers.TryGetValue(name, out var value) ? value : null, null);
            var response = filter.FilterResponseHeaders(headers, null);

            // Then
            CollectionAssert.AreEquivalent(new[] { "cache-control", "content-type" }, request.Keys);
            CollectionAssert.AreEquivalent(defaultResponseHeaders, response.Keys);
        }

        [Test]
        public void CustomCapturesTheDefaultHeadersFirstOnlyWhenIncluded()
        {
            // Given
            // The custom headers alone fill the 2 KB budget
            var bigNames = Enumerable.Range(0, 20).Select(i => $"x-big-{i:D2}").ToList();
            var withDefaults = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, bigNames, includeDefaultHeaders: true);
            var withoutDefaults = new ResourceHeaderFilter(TrackResourceHeadersMode.Custom, bigNames);
            var headers = bigNames.ToDictionary(name => name, _ => new string('v', 128));
            headers["etag"] = "\"1\"";

            // When
            var resultWithDefaults = withDefaults.FilterResponseHeaders(headers, null);
            var resultWithoutDefaults = withoutDefaults.FilterResponseHeaders(headers, null);

            // Then
            Assert.AreEqual("\"1\"", resultWithDefaults["etag"]);
            Assert.IsFalse(resultWithoutDefaults.ContainsKey("etag"));
        }
    }
}
