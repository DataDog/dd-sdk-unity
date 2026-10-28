// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;

namespace Datadog.Unity.Rum
{
    /// <summary>
    /// Selects, sanitizes and bounds the HTTP headers that are attached to a RUM resource, mirroring the native
    /// iOS (HeaderProcessor) and Android (ResourceHeadersExtractor) implementations.
    /// </summary>
    internal class ResourceHeaderFilter
    {
        public const int MaxValueBytes = 128;
        public const int MaxHeaderCount = 100;
        public const int MaxTotalBytes = 2048;

        public const string RequestHeadersAttribute = "_dd.request_headers";
        public const string ResponseHeadersAttribute = "_dd.response_headers";

        internal static readonly string[] DefaultRequestHeaders =
        {
            "cache-control",
            "content-type",
        };

        internal static readonly string[] DefaultResponseHeaders =
        {
            "cache-control",
            "etag",
            "age",
            "expires",
            "content-type",
            "content-encoding",
            "content-length",
            "vary",
            "server-timing",
            "x-cache",
            "cf-cache-status",
            "x-vercel-cache",
        };

        private static readonly Regex SensitiveHeaderPattern = new Regex(
            "(token|cookie|secret|authorization|password|credential|bearer|(api|secret|access|app).?key|forwarded|real.?ip|connecting.?ip|client.?ip)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly TrackResourceHeadersMode _mode;
        private readonly List<string> _requestHeaderNames = new List<string>();
        private readonly List<string> _responseHeaderNames = new List<string>();

        public ResourceHeaderFilter(TrackResourceHeadersMode mode, IEnumerable<string> customHeaders = null, bool includeDefaultHeaders = false)
        {
            _mode = mode;
            if (mode == TrackResourceHeadersMode.Disabled)
            {
                return;
            }

            // Defaults are listed first so that they take priority when the size budget runs out
            if (mode == TrackResourceHeadersMode.Defaults || includeDefaultHeaders)
            {
                _requestHeaderNames.AddRange(DefaultRequestHeaders);
                _responseHeaderNames.AddRange(DefaultResponseHeaders);
            }

            if (mode != TrackResourceHeadersMode.Custom || customHeaders == null)
            {
                return;
            }

            foreach (var name in customHeaders)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // Sensitive headers are never captured, even when explicitly listed
                var lowercased = name.Trim().ToLowerInvariant();
                if (SensitiveHeaderPattern.IsMatch(lowercased))
                {
                    continue;
                }

                if (!_requestHeaderNames.Contains(lowercased))
                {
                    _requestHeaderNames.Add(lowercased);
                }

                if (!_responseHeaderNames.Contains(lowercased))
                {
                    _responseHeaderNames.Add(lowercased);
                }
            }
        }

        public bool IsEnabled => _requestHeaderNames.Count > 0 || _responseHeaderNames.Count > 0;

        /// <summary>
        /// Converts the configuration to the Browser SDK's `trackResourceHeaders` init option, used on WebGL where the
        /// Browser SDK captures resources itself.
        /// </summary>
        /// <returns>The option value, or null when header capture is disabled.</returns>
        public object ToBrowserSdkOption()
        {
            if (!IsEnabled)
            {
                return null;
            }

            if (_mode == TrackResourceHeadersMode.Defaults)
            {
                return true;
            }

            // The Browser SDK cannot combine its own defaults with custom matchers, so defaults are listed by name
            return _requestHeaderNames.Union(_responseHeaderNames)
                .Select(name => new Dictionary<string, object> { { "name", name } })
                .ToList();
        }

        /// <summary>
        /// Captures request headers. UnityWebRequest cannot list its request headers, so each wanted header is read
        /// by name through <paramref name="getHeader"/>, which returns null when the header is not set.
        /// </summary>
        /// <returns>The captured headers, or null if there are none.</returns>
        public Dictionary<string, object> FilterRequestHeaders(Func<string, string> getHeader, IInternalLogger logger)
        {
            if (!IsEnabled || getHeader == null)
            {
                return null;
            }

            return Filter(_requestHeaderNames, getHeader, logger);
        }

        /// <summary>
        /// Captures response headers. Key casing differs between platforms, so lookups are case-insensitive.
        /// </summary>
        /// <returns>The captured headers, or null if there are none.</returns>
        public Dictionary<string, object> FilterResponseHeaders(IDictionary<string, string> responseHeaders, IInternalLogger logger)
        {
            if (!IsEnabled || responseHeaders == null)
            {
                return null;
            }

            var normalized = new Dictionary<string, string>();
            foreach (var header in responseHeaders)
            {
                if (header.Key != null && header.Value != null)
                {
                    normalized[header.Key.ToLowerInvariant()] = header.Value;
                }
            }

            return Filter(_responseHeaderNames, name => normalized.TryGetValue(name, out var value) ? value : null, logger);
        }

        internal static string TruncateToBytes(string value, int maxBytes)
        {
            if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            {
                return value;
            }

            // Walk the string in code points so a multi-byte character is never cut in half
            var bytes = 0;
            var end = 0;
            while (end < value.Length)
            {
                var length = char.IsHighSurrogate(value[end]) && end + 1 < value.Length && char.IsLowSurrogate(value[end + 1]) ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(value.Substring(end, length));
                if (bytes + size > maxBytes)
                {
                    break;
                }

                bytes += size;
                end += length;
            }

            return value.Substring(0, end);
        }

        private static Dictionary<string, object> Filter(List<string> names, Func<string, string> getHeader, IInternalLogger logger)
        {
            // The Android bridge only converts Dictionary<string, object> to a Java map
            var result = new Dictionary<string, object>();
            var totalBytes = 0;

            foreach (var name in names)
            {
                if (result.Count >= MaxHeaderCount)
                {
                    break;
                }

                var value = getHeader(name);
                if (value == null)
                {
                    continue;
                }

                var truncated = TruncateToBytes(value, MaxValueBytes);
                var entryBytes = Encoding.UTF8.GetByteCount(name) + Encoding.UTF8.GetByteCount(truncated);
                if (totalBytes + entryBytes > MaxTotalBytes)
                {
                    logger?.Log(DdLogLevel.Warn, $"Skipping header '{name}': adding it would exceed the {MaxTotalBytes} byte limit.");
                    continue;
                }

                result[name] = truncated;
                totalBytes += entryBytes;
            }

            return result.Count > 0 ? result : null;
        }
    }
}
