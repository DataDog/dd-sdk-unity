// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Text;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Newtonsoft.Json;
using UnityEngine.Scripting;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Writes serialized flag configuration to a <see cref="IKeyValueStore"/> under a scoped key.
    /// Key format: dd_flags_v1_{site}_{env}_{clientToken[-8:]}
    /// Payload is wrapped in a <see cref="FlagsCacheEnvelopeDto"/> before serialization.
    /// Size gate: payloads >= 500 KB are skipped (warn log); payloads >= 400 KB log a warn but write.
    /// </summary>
    internal class FlagsCacheStore : IFlagsCacheWriter
    {
        internal const string KeyPrefix = "dd_flags_v1";
        internal const int WarnThresholdBytes = 400 * 1024;
        internal const int SkipThresholdBytes = 500 * 1024;

        private readonly IKeyValueStore _store;
        private readonly string _site;
        private readonly string _env;
        private readonly string _clientToken;
        private readonly IInternalLogger _logger;

        internal FlagsCacheStore(
            IKeyValueStore store,
            string site,
            string env,
            string clientToken,
            IInternalLogger logger)
        {
            _store = store;
            _site = site;
            _env = env;
            _clientToken = clientToken;
            _logger = logger;
        }

        /// <summary>
        /// Computes the PlayerPrefs key for the given site, env, and client token.
        /// Uses the last 8 characters of clientToken if its length >= 8; verbatim otherwise.
        /// Null token is treated as an empty string.
        /// </summary>
        internal static string ComputeKey(string site, string env, string clientToken)
        {
            var token = clientToken ?? string.Empty;
            var suffix = token.Length >= 8 ? token.Substring(token.Length - 8) : token;
            return $"{KeyPrefix}_{site}_{env}_{suffix}";
        }

        /// <inheritdoc/>
        public void Write(string rawJson, FlagsEvaluationContext context)
        {
            try
            {
                var envelope = new FlagsCacheEnvelopeDto
                {
                    CachedAt = DateTimeOffset.UtcNow.ToString("o"),
                    Payload = rawJson,
                };

                var serialized = JsonConvert.SerializeObject(envelope);
                var byteCount = Encoding.UTF8.GetByteCount(serialized);

                if (byteCount >= SkipThresholdBytes)
                {
                    _logger?.Log(DdLogLevel.Warn,
                        $"[Flags] Cache write skipped: payload {byteCount} bytes exceeds {SkipThresholdBytes} byte limit.");
                    return;
                }

                if (byteCount >= WarnThresholdBytes)
                {
                    _logger?.Log(DdLogLevel.Warn,
                        $"[Flags] Cache payload is large ({byteCount} bytes); approaching tvOS NSUserDefaults limit.");
                }

                var key = ComputeKey(_site, _env, _clientToken);
                _store.SetString(key, serialized);
                _store.Save();
            }
            catch (Exception e)
            {
                _logger?.Log(DdLogLevel.Warn, $"[Flags] Cache write failed: {e.Message}");
            }
        }

        /// <summary>
        /// Forces AOT compilation of the FlagsCacheEnvelopeDto deserialization path used by Phase 2
        /// bootstrap. Without this hint, IL2CPP strips DeserializeObject&lt;FlagsCacheEnvelopeDto&gt;
        /// on tvOS/iOS because no reachable call site is visible at link time.
        /// This method is never called at runtime — [Preserve] prevents the tree-shaker from
        /// removing the generated generic deserialization code.
        /// </summary>
        [Preserve]
        private static void EnsureTypes()
        {
            // Force AOT compilation of the envelope deserialization path used by Phase 2 bootstrap.
            // Without this, IL2CPP strips DeserializeObject<FlagsCacheEnvelopeDto> on tvOS/iOS.
            _ = Newtonsoft.Json.JsonConvert.DeserializeObject<FlagsCacheEnvelopeDto>(string.Empty);
        }
    }
}
