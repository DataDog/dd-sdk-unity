// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Flags
{
    /// <summary>Public response encoding metadata. This is obfuscation, not encryption.</summary>
    internal sealed class FlagKeyObfuscation
    {
        internal const string Scheme = "flag-key-sha256-v1";
        internal const string Capability = "assignment-encoding-flag-key-256-v1";
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private static readonly byte[] Domain = StrictUtf8.GetBytes("datadog.feature-flags.flag-key.v1\0");
        private readonly byte[] _prefix;

        private FlagKeyObfuscation(string salt)
        {
            _prefix = new byte[Domain.Length + 16];
            Array.Copy(Domain, _prefix, Domain.Length);
            for (var i = 0; i < 16; i++)
                _prefix[Domain.Length + i] = Convert.ToByte(salt.Substring(i * 2, 2), 16);
        }

        internal static FlagKeyObfuscation Read(JToken marker, JToken descriptor)
        {
            if ((marker == null || (marker.Type == JTokenType.Boolean && !marker.Value<bool>())) && descriptor == null)
                return null;

            if (marker?.Type != JTokenType.Boolean || !marker.Value<bool>() || !(descriptor is JObject metadata))
                throw new JsonSerializationException("Invalid flag-key obfuscation metadata.");

            if (metadata["scheme"]?.Type != JTokenType.String || metadata["scheme"].Value<string>() != Scheme)
                throw new JsonSerializationException("Unsupported flag-key obfuscation scheme.");

            var salt = metadata["salt"];
            if (salt?.Type != JTokenType.String || !IsLowercaseHex(salt.Value<string>(), 32))
                throw new JsonSerializationException("Flag-key salt must contain 32 lowercase hexadecimal characters.");

            return new FlagKeyObfuscation(salt.Value<string>());
        }

        internal string Encode(string key)
        {
            // Reject invalid UTF-16 rather than aliasing a key containing a replacement character.
            var keyBytes = StrictUtf8.GetBytes(key);
            var input = new byte[_prefix.Length + keyBytes.Length];
            Array.Copy(_prefix, input, _prefix.Length);
            Array.Copy(keyBytes, 0, input, _prefix.Length, keyBytes.Length);
            using var sha256 = SHA256.Create();
            var digest = sha256.ComputeHash(input);
            var result = new StringBuilder(64);
            foreach (var value in digest)
                result.Append(value.ToString("x2"));
            return result.ToString();
        }

        internal static bool IsLowercaseHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    return false;
            return true;
        }
    }
}
