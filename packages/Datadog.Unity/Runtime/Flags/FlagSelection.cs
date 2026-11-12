// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Text;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Selects which flags the SDK loads. A flag is loaded when it has any of the <see cref="Tags"/>
    /// or its key matches any of the <see cref="KeyPatterns"/>.
    /// </summary>
    /// <remarks>
    /// Values are trimmed of surrounding whitespace, and empty and duplicate values are ignored.
    /// Each list accepts at most 31 values, and each value is at most 200 UTF-8 bytes.
    /// A key pattern matches the whole flag key, and <c>*</c> matches any run of characters.
    /// If the selection breaks a limit, the SDK logs an error and loads every flag.
    /// A custom flags endpoint must forward the <c>DD-Flag-Selection-Ignored</c> response header
    /// for a selection the server ignored to be reported.
    /// </remarks>
    public sealed class FlagSelection
    {
        /// <summary>
        /// Flag tags to select. Null or empty selects no flags by tag.
        /// </summary>
        public readonly string[] Tags;

        /// <summary>
        /// Flag key patterns to select. Null or empty selects no flags by key.
        /// </summary>
        public readonly string[] KeyPatterns;

        public FlagSelection(string[] tags = null, string[] keyPatterns = null)
        {
            Tags = (string[])tags?.Clone();
            KeyPatterns = (string[])keyPatterns?.Clone();
        }
    }

    /// <summary>
    /// A flag selection that is trimmed, deduplicated, sorted by UTF-8 bytes, and within the limits.
    /// </summary>
    internal sealed class CanonicalFlagSelection
    {
        public readonly string[] Tags;
        public readonly string[] KeyPatterns;

        public CanonicalFlagSelection(string[] tags, string[] keyPatterns)
        {
            Tags = tags;
            KeyPatterns = keyPatterns;
        }
    }

    /// <summary>
    /// Reasons a flag selection is dropped. Their codes match the server and the other Datadog SDKs.
    /// </summary>
    internal enum FlagSelectionError
    {
        MalformedSelection,
        TooManyValues,
        InvalidKeyPattern,
        InvalidTag,
    }

    internal sealed class FlagSelectionResult
    {
        public static readonly FlagSelectionResult None = new(null, null);

        private FlagSelectionResult(CanonicalFlagSelection selection, FlagSelectionError? error)
        {
            Selection = selection;
            Error = error;
        }

        /// <summary>
        /// The selection to send, or null when no selection is sent.
        /// </summary>
        public CanonicalFlagSelection Selection { get; }

        /// <summary>
        /// The reason the configured selection was dropped, or null when it was not.
        /// </summary>
        public FlagSelectionError? Error { get; }

        public static FlagSelectionResult Selected(CanonicalFlagSelection selection) => new(selection, null);

        public static FlagSelectionResult Dropped(FlagSelectionError error) => new(null, error);
    }

    /// <summary>
    /// Canonicalizes and validates a flag selection with the same rules as the server.
    /// </summary>
    internal static class FlagSelectionCanonicalizer
    {
        public const int MaxValues = 31;
        public const int MaxTagBytes = 200;
        public const int MaxKeyPatternBytes = 200;

        public static FlagSelectionResult Canonicalize(FlagSelection selection)
        {
            if (selection == null)
            {
                return FlagSelectionResult.None;
            }

            if (!IsWellFormed(selection.Tags) || !IsWellFormed(selection.KeyPatterns))
            {
                return FlagSelectionResult.Dropped(FlagSelectionError.MalformedSelection);
            }

            var tags = CanonicalizeList(selection.Tags);
            var keyPatterns = CanonicalizeList(selection.KeyPatterns);

            if (tags.Count > MaxValues || keyPatterns.Count > MaxValues)
            {
                return FlagSelectionResult.Dropped(FlagSelectionError.TooManyValues);
            }

            if (AnyLongerThan(keyPatterns, MaxKeyPatternBytes))
            {
                return FlagSelectionResult.Dropped(FlagSelectionError.InvalidKeyPattern);
            }

            if (AnyLongerThan(tags, MaxTagBytes))
            {
                return FlagSelectionResult.Dropped(FlagSelectionError.InvalidTag);
            }

            if (tags.Count == 0 && keyPatterns.Count == 0)
            {
                return FlagSelectionResult.None;
            }

            return FlagSelectionResult.Selected(new CanonicalFlagSelection(SortByUtf8(tags), SortByUtf8(keyPatterns)));
        }

        // The Unicode White_Space property, which the server trims. string.Trim() differs: it also trims
        // U+001C..U+001F and does not trim U+0085.
        internal static bool IsWhiteSpace(char c)
        {
            switch (c)
            {
                case '\u0009':
                case '\u000A':
                case '\u000B':
                case '\u000C':
                case '\u000D':
                case '\u0020':
                case '\u0085':
                case '\u00A0':
                case '\u1680':
                case '\u2028':
                case '\u2029':
                case '\u202F':
                case '\u205F':
                case '\u3000':
                    return true;
                default:
                    return c >= '\u2000' && c <= '\u200A';
            }
        }

        internal static string Trim(string value)
        {
            var start = 0;
            var end = value.Length;
            while (start < end && IsWhiteSpace(value[start]))
            {
                start++;
            }

            while (end > start && IsWhiteSpace(value[end - 1]))
            {
                end--;
            }

            return value.Substring(start, end - start);
        }

        private static bool IsWellFormed(string[] values)
        {
            if (values == null)
            {
                return true;
            }

            foreach (var value in values)
            {
                if (value == null || HasUnpairedSurrogate(value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasUnpairedSurrogate(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                }
                else if (char.IsSurrogate(value[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> CanonicalizeList(string[] values)
        {
            var result = new List<string>();
            if (values == null)
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                var trimmed = Trim(value);
                if (trimmed.Length > 0 && seen.Add(trimmed))
                {
                    result.Add(trimmed);
                }
            }

            return result;
        }

        private static bool AnyLongerThan(List<string> values, int maxBytes)
        {
            foreach (var value in values)
            {
                if (Encoding.UTF8.GetByteCount(value) > maxBytes)
                {
                    return true;
                }
            }

            return false;
        }

        // Ordinal string comparison orders UTF-16 code units, which differs from UTF-8 byte order for
        // characters outside the Basic Multilingual Plane.
        private static string[] SortByUtf8(List<string> values)
        {
            var keyed = new List<KeyValuePair<byte[], string>>(values.Count);
            foreach (var value in values)
            {
                keyed.Add(new KeyValuePair<byte[], string>(Encoding.UTF8.GetBytes(value), value));
            }

            keyed.Sort((left, right) => CompareBytes(left.Key, right.Key));
            var sorted = new string[keyed.Count];
            for (var i = 0; i < keyed.Count; i++)
            {
                sorted[i] = keyed[i].Value;
            }

            return sorted;
        }

        private static int CompareBytes(byte[] left, byte[] right)
        {
            var length = Math.Min(left.Length, right.Length);
            for (var i = 0; i < length; i++)
            {
                if (left[i] != right[i])
                {
                    return left[i].CompareTo(right[i]);
                }
            }

            return left.Length.CompareTo(right.Length);
        }
    }

    /// <summary>
    /// Sanitizes the <c>DD-Flag-Selection-Ignored</c> response header, so a raw header value never reaches
    /// logs or telemetry.
    /// </summary>
    internal static class FlagSelectionIgnoredReason
    {
        public const string HeaderName = "DD-Flag-Selection-Ignored";
        public const string Unknown = "unknown";

        // The longest known reason has 19 characters. The limit bounds the work on an oversized header.
        private const int MaxLength = 32;

        private static readonly HashSet<string> KnownReasons = new(StringComparer.Ordinal)
        {
            "malformed_selection",
            "too_many_values",
            "invalid_key_pattern",
            "invalid_tag",
            "tag_load_failed",
        };

        /// <summary>
        /// Returns a known reason, <see cref="Unknown"/> for any other value, or null when the header is
        /// absent or empty. A repeated header arrives joined with commas, so it reports <see cref="Unknown"/>.
        /// </summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            if (value.Length > MaxLength)
            {
                return Unknown;
            }

            return KnownReasons.Contains(value) ? value : Unknown;
        }
    }

    /// <summary>
    /// Logs dropped and ignored flag selections, and sends their debug telemetry once per reason.
    /// Logs and telemetry carry only reason codes, never selection values.
    /// </summary>
    internal sealed class FlagSelectionReporter
    {
        internal const string DroppedTelemetryMessage = "Flag selection dropped";
        internal const string IgnoredTelemetryMessage = "Flag selection ignored";
        private const string ReasonAttribute = "reason";

        private readonly IInternalLogger _logger;
        private readonly HashSet<string> _sentTelemetry = new(StringComparer.Ordinal);
        private readonly object _lock = new();

        public FlagSelectionReporter(IInternalLogger logger)
        {
            _logger = logger;
        }

        internal static string Reason(FlagSelectionError error)
        {
            switch (error)
            {
                case FlagSelectionError.MalformedSelection:
                    return "malformed_selection";
                case FlagSelectionError.TooManyValues:
                    return "too_many_values";
                case FlagSelectionError.InvalidKeyPattern:
                    return "invalid_key_pattern";
                default:
                    return "invalid_tag";
            }
        }

        internal static string Limit(FlagSelectionError error)
        {
            switch (error)
            {
                case FlagSelectionError.MalformedSelection:
                    return "flagSelection must have optional Tags and KeyPatterns arrays of non-null strings " +
                           "without unpaired UTF-16 surrogates";
                case FlagSelectionError.TooManyValues:
                    return $"each list accepts at most {FlagSelectionCanonicalizer.MaxValues} values";
                case FlagSelectionError.InvalidKeyPattern:
                    return $"each key pattern is at most {FlagSelectionCanonicalizer.MaxKeyPatternBytes} UTF-8 bytes";
                default:
                    return $"each tag is at most {FlagSelectionCanonicalizer.MaxTagBytes} UTF-8 bytes";
            }
        }

        public void ReportDropped(FlagSelectionError error)
        {
            var reason = Reason(error);
            Report(
                $"flagSelection was not sent ({reason}: {Limit(error)}). Loading the full flag configuration.",
                DroppedTelemetryMessage,
                reason);
        }

        public void ReportIgnored(string reason)
        {
            Report(
                $"flagSelection was ignored by the server ({reason}). Loaded the full flag configuration.",
                IgnoredTelemetryMessage,
                reason);
        }

        private void Report(string logMessage, string telemetryMessage, string reason)
        {
            try
            {
                _logger?.Log(DdLogLevel.Error, logMessage);

                bool firstReport;
                lock (_lock)
                {
                    firstReport = _sentTelemetry.Add($"{telemetryMessage}:{reason}");
                }

                if (firstReport)
                {
                    _logger?.TelemetryDebug(telemetryMessage, new Dictionary<string, object> { { ReasonAttribute, reason } });
                }
            }
            catch (Exception)
            {
                // A diagnostic must never interrupt flag loading.
            }
        }
    }
}
