// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

namespace Datadog.Unity.Flags.Tests
{
    public class FlagSelectionTests
    {
#if UNITY_EDITOR
        private static JObject LoadSelectionVectors()
        {
            var script = AssetDatabase.FindAssets("FlagSelectionTests t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath).Single();
            var path = Path.Combine(Path.GetDirectoryName(script), "Fixtures", "flag-selection", "selection-vectors.json");

            // Vector values are raw strings; date parsing would rewrite values that look like dates.
            return JsonConvert.DeserializeObject<JObject>(
                File.ReadAllText(path),
                new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
        }

        public static IEnumerable<TestCaseData> SelectionVectors()
        {
            foreach (var vector in LoadSelectionVectors()["cases"].Cast<JObject>())
            {
                var data = new TestCaseData(vector).SetName($"SelectionVector({(string)vector["name"]})");

                // rawQuery cases cover how the server decodes a URL query, so they have no SDK expectation.
                if (vector["sdk"] == null)
                {
                    data.Ignore("Server-only rawQuery case");
                }

                yield return data;
            }
        }

        [Test]
        public void SelectionVectorsHaveAnSdkExpectationForEveryQueryCase()
        {
            var cases = LoadSelectionVectors()["cases"].Cast<JObject>().ToList();

            Assert.That(cases.Count(c => c["sdk"] != null), Is.GreaterThan(0));
            foreach (var vector in cases)
            {
                var name = (string)vector["name"];
                Assert.AreNotEqual(vector["query"] == null, vector["rawQuery"] == null, name);
                Assert.AreEqual(vector["query"] != null, vector["sdk"] != null, name);
            }
        }

        [TestCaseSource(nameof(SelectionVectors))]
        public void CanonicalizesLikeTheSelectionVectors(JObject vector)
        {
            var query = vector["query"];
            var selection = new FlagSelection(
                tags: query["selected_flag_tags"]?.ToObject<string[]>(),
                keyPatterns: query["selected_flag_key_patterns"]?.ToObject<string[]>());

            var result = FlagSelectionCanonicalizer.Canonicalize(selection);

            var sdk = vector["sdk"];
            var dropped = (string)sdk["dropped"];
            if (dropped != null)
            {
                Assert.IsNull(result.Selection);
                Assert.IsTrue(result.Error.HasValue);
                Assert.AreEqual(dropped, FlagSelectionReporter.Reason(result.Error.Value));
                return;
            }

            Assert.IsNull(result.Error);
            var expectedTags = sdk["selection"]["tags"].ToObject<string[]>();
            var expectedKeyPatterns = sdk["selection"]["keyPatterns"].ToObject<string[]>();
            if (expectedTags.Length == 0 && expectedKeyPatterns.Length == 0)
            {
                Assert.IsNull(result.Selection);
                return;
            }

            CollectionAssert.AreEqual(expectedTags, result.Selection.Tags);
            CollectionAssert.AreEqual(expectedKeyPatterns, result.Selection.KeyPatterns);
        }
#endif

        // ─── Canonicalization ────────────────────────────────────────────────────────

        [Test]
        public void NoSelectionSendsNothing()
        {
            Assert.AreSame(FlagSelectionResult.None, FlagSelectionCanonicalizer.Canonicalize(null));
            Assert.AreSame(FlagSelectionResult.None, FlagSelectionCanonicalizer.Canonicalize(new FlagSelection()));
            Assert.AreSame(FlagSelectionResult.None, FlagSelectionCanonicalizer.Canonicalize(
                new FlagSelection(new[] { " ", "" }, new string[0])));
        }

        public static IEnumerable<TestCaseData> MalformedSelections()
        {
            yield return new TestCaseData(new FlagSelection(new[] { "a", null })).SetName("NullTag");
            yield return new TestCaseData(new FlagSelection(keyPatterns: new string[] { null })).SetName("NullKeyPattern");
            yield return new TestCaseData(new FlagSelection(new[] { "a\uD800" })).SetName("UnpairedHighSurrogate");
            yield return new TestCaseData(new FlagSelection(keyPatterns: new[] { "\uDC00b" })).SetName("UnpairedLowSurrogate");
            yield return new TestCaseData(new FlagSelection(new[] { "\uDC00\uD800" })).SetName("ReversedSurrogatePair");
            yield return new TestCaseData(new FlagSelection(
                    Enumerable.Range(0, 40).Select(i => $"t{i}").ToArray(),
                    new string[] { null }))
                .SetName("MalformedTakesPrecedenceOverTooManyValues");
            yield return new TestCaseData(new FlagSelection(new[] { new string('a', 201) }, new[] { "\uD800" }))
                .SetName("MalformedTakesPrecedenceOverInvalidTag");
        }

        [TestCaseSource(nameof(MalformedSelections))]
        public void DropsMalformedSelection(FlagSelection selection)
        {
            var result = FlagSelectionCanonicalizer.Canonicalize(selection);

            Assert.IsNull(result.Selection);
            Assert.AreEqual(FlagSelectionError.MalformedSelection, result.Error);
        }

        [Test]
        public void KeepsWellFormedSurrogatePairs()
        {
            var result = FlagSelectionCanonicalizer.Canonicalize(new FlagSelection(new[] { "\uD83D\uDE00" }));

            CollectionAssert.AreEqual(new[] { "\uD83D\uDE00" }, result.Selection.Tags);
        }

        // An independent list of the 25 Unicode White_Space code points.
        private static readonly char[] WhiteSpace =
        {
            '\u0009', '\u000A', '\u000B', '\u000C', '\u000D', '\u0020', '\u0085', '\u00A0', '\u1680',
            '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008',
            '\u2009', '\u200A', '\u2028', '\u2029', '\u202F', '\u205F', '\u3000',
        };

        [Test]
        public void TrimsEveryWhiteSpaceCharacter([ValueSource(nameof(WhiteSpace))] char whiteSpace)
        {
            var result = FlagSelectionCanonicalizer.Canonicalize(new FlagSelection(new[] { $"{whiteSpace}a{whiteSpace}" }));

            CollectionAssert.AreEqual(new[] { "a" }, result.Selection.Tags);
        }

        [Test]
        [TestCase('\u001C')]
        [TestCase('\u001D')]
        [TestCase('\u001E')]
        [TestCase('\u001F')]
        [TestCase('\u200B')]
        [TestCase('\uFEFF')]
        public void KeepsCharactersOutsideWhiteSpace(char character)
        {
            var value = $"{character}a{character}";
            var result = FlagSelectionCanonicalizer.Canonicalize(new FlagSelection(new[] { value }));

            CollectionAssert.AreEqual(new[] { value }, result.Selection.Tags);
        }

        [Test]
        public void KeepsInteriorWhiteSpaceAndDedupesAfterTrimming()
        {
            var result = FlagSelectionCanonicalizer.Canonicalize(
                new FlagSelection(new[] { " a  b ", "a  b", "\u3000a  b" }));

            CollectionAssert.AreEqual(new[] { "a  b" }, result.Selection.Tags);
        }

        [Test]
        public void SortsByUtf8BytesRatherThanUtf16CodeUnits()
        {
            // U+FF61 encodes as EF BD A1 and U+1F600 as F0 9F 98 80, but U+1F600's UTF-16 surrogate D83D sorts first.
            var result = FlagSelectionCanonicalizer.Canonicalize(
                new FlagSelection(keyPatterns: new[] { "\uD83D\uDE00", "\uFF61", "b", "a" }));

            CollectionAssert.AreEqual(new[] { "a", "b", "\uFF61", "\uD83D\uDE00" }, result.Selection.KeyPatterns);
        }

        [Test]
        public void AppliesLimitsInOrder()
        {
            var tooMany = Enumerable.Range(0, 32).Select(i => $"v{i}").ToArray();
            var longValue = new string('a', 201);

            Assert.AreEqual(FlagSelectionError.TooManyValues,
                FlagSelectionCanonicalizer.Canonicalize(new FlagSelection(tooMany, new[] { longValue })).Error);
            Assert.AreEqual(FlagSelectionError.InvalidKeyPattern,
                FlagSelectionCanonicalizer.Canonicalize(new FlagSelection(new[] { longValue }, new[] { longValue })).Error);
            Assert.AreEqual(FlagSelectionError.InvalidTag,
                FlagSelectionCanonicalizer.Canonicalize(new FlagSelection(new[] { longValue })).Error);
        }

        [Test]
        public void AcceptsValuesAtTheLimits()
        {
            var values = Enumerable.Range(0, 31).Select(i => $"{i:D2}{new string('\u00E9', 99)}").ToArray();

            var result = FlagSelectionCanonicalizer.Canonicalize(new FlagSelection(values, values));

            Assert.IsNull(result.Error);
            Assert.AreEqual(31, result.Selection.Tags.Length);
        }

        [Test]
        public void CopiesTheConfiguredLists()
        {
            var tags = new[] { "a" };
            var selection = new FlagSelection(tags);

            tags[0] = "b";

            CollectionAssert.AreEqual(new[] { "a" }, selection.Tags);
        }

        // ─── Ignored header ──────────────────────────────────────────────────────────

        [Test]
        [TestCase("malformed_selection")]
        [TestCase("too_many_values")]
        [TestCase("invalid_key_pattern")]
        [TestCase("invalid_tag")]
        [TestCase("tag_load_failed")]
        public void KeepsKnownIgnoredReasons(string reason)
        {
            Assert.AreEqual(reason, FlagSelectionIgnoredReason.Sanitize(reason));
        }

        [Test]
        [TestCase("something_new")]
        [TestCase("invalid_tag, invalid_tag")]
        [TestCase("invalid_tag,too_many_values")]
        [TestCase(" invalid_tag")]
        [TestCase("INVALID_TAG")]
        [TestCase("invalid_tag_________________________________")]
        public void ReportsOtherIgnoredValuesAsUnknown(string value)
        {
            Assert.AreEqual("unknown", FlagSelectionIgnoredReason.Sanitize(value));
        }

        [Test]
        [TestCase(null)]
        [TestCase("")]
        public void TreatsAnEmptyIgnoredHeaderAsAbsent(string value)
        {
            Assert.IsNull(FlagSelectionIgnoredReason.Sanitize(value));
        }

        // ─── Request body ────────────────────────────────────────────────────────────

        [Test]
        public void RequestBodyCarriesTheCanonicalSelection()
        {
            var selection = new CanonicalFlagSelection(new[] { "checkout" }, new string[0]);

            var body = JObject.Parse(PrecomputeAssignmentsFetcher.BuildRequestBody(
                "prod", new FlagsEvaluationContext("user-1"), selection));

            var flagSelection = (JObject)body["data"]["attributes"]["flag_selection"];
            Assert.AreEqual(
                JObject.Parse(@"{""tags"": [""checkout""], ""key_patterns"": []}").ToString(),
                flagSelection.ToString());
        }

        [Test]
        public void RequestBodyOmitsFlagSelectionWithoutASelection()
        {
            var body = JObject.Parse(PrecomputeAssignmentsFetcher.BuildRequestBody(
                "prod", new FlagsEvaluationContext("user-1"), null));

            Assert.IsNull(body["data"]["attributes"]["flag_selection"]);
        }

        [Test]
        public void TryParseResponseReportsWhetherTheBodyHadFlags()
        {
            Assert.IsTrue(PrecomputeAssignmentsFetcher.TryParseResponse(
                @"{""data"": {""attributes"": {""flags"": {}}}}", out _));
            Assert.IsFalse(PrecomputeAssignmentsFetcher.TryParseResponse("not json", out var flags));
            Assert.AreEqual(0, flags.Count);
            Assert.IsFalse(PrecomputeAssignmentsFetcher.TryParseResponse(@"{""data"": {}}", out _));
        }

        // ─── Reporting ───────────────────────────────────────────────────────────────

        [Test]
        public void ReportsADroppedSelection()
        {
            var logger = Substitute.For<IInternalLogger>();

            new FlagSelectionReporter(logger).ReportDropped(FlagSelectionError.InvalidTag);

            logger.Received(1).Log(DdLogLevel.Error,
                "flagSelection was not sent (invalid_tag: each tag is at most 200 UTF-8 bytes). " +
                "Loading the full flag configuration.");
            logger.Received(1).TelemetryDebug("Flag selection dropped",
                Arg.Is<IDictionary<string, object>>(a => a.Count == 1 && (string)a["reason"] == "invalid_tag"));
        }

        [Test]
        [TestCase(nameof(FlagSelectionError.MalformedSelection),
            "malformed_selection: flagSelection must have optional Tags and KeyPatterns arrays of non-null strings " +
            "without unpaired UTF-16 surrogates")]
        [TestCase(nameof(FlagSelectionError.TooManyValues), "too_many_values: each list accepts at most 31 values")]
        [TestCase(nameof(FlagSelectionError.InvalidKeyPattern),
            "invalid_key_pattern: each key pattern is at most 200 UTF-8 bytes")]
        public void DescribesEachLimit(string error, string description)
        {
            var logger = Substitute.For<IInternalLogger>();

            new FlagSelectionReporter(logger).ReportDropped(
                (FlagSelectionError)Enum.Parse(typeof(FlagSelectionError), error));

            logger.Received(1).Log(DdLogLevel.Error,
                $"flagSelection was not sent ({description}). Loading the full flag configuration.");
        }

        [Test]
        public void ReportsAnIgnoredSelection()
        {
            var logger = Substitute.For<IInternalLogger>();

            new FlagSelectionReporter(logger).ReportIgnored("tag_load_failed");

            logger.Received(1).Log(DdLogLevel.Error,
                "flagSelection was ignored by the server (tag_load_failed). Loaded the full flag configuration.");
            logger.Received(1).TelemetryDebug("Flag selection ignored",
                Arg.Is<IDictionary<string, object>>(a => (string)a["reason"] == "tag_load_failed"));
        }

        [Test]
        public void SendsTelemetryOncePerReasonAndLogsEveryReport()
        {
            var logger = Substitute.For<IInternalLogger>();
            var reporter = new FlagSelectionReporter(logger);

            reporter.ReportIgnored("unknown");
            reporter.ReportIgnored("unknown");
            reporter.ReportIgnored("invalid_tag");

            logger.Received(3).Log(DdLogLevel.Error, Arg.Any<string>());
            logger.Received(1).TelemetryDebug("Flag selection ignored",
                Arg.Is<IDictionary<string, object>>(a => (string)a["reason"] == "unknown"));
            logger.Received(1).TelemetryDebug("Flag selection ignored",
                Arg.Is<IDictionary<string, object>>(a => (string)a["reason"] == "invalid_tag"));
        }

        [Test]
        public void ReportingNeverThrows()
        {
            var logger = Substitute.For<IInternalLogger>();
            logger.When(l => l.Log(Arg.Any<DdLogLevel>(), Arg.Any<string>())).Do(_ => throw new InvalidOperationException());

            Assert.DoesNotThrow(() => new FlagSelectionReporter(logger).ReportDropped(FlagSelectionError.InvalidTag));
        }

        [Test]
        public void ResolvingADroppedSelectionReportsItAndSendsNoSelection()
        {
            var logger = Substitute.For<IInternalLogger>();

            var selection = DdFlags.ResolveFlagSelection(
                new FlagSelection(new[] { (string)null }), new FlagSelectionReporter(logger));

            Assert.IsNull(selection);
            logger.Received(1).Log(DdLogLevel.Error, Arg.Is<string>(m => m.StartsWith("flagSelection was not sent (malformed_selection: ")));
        }

        [Test]
        public void ResolvingAValidSelectionReportsNothing()
        {
            var logger = Substitute.For<IInternalLogger>();

            var selection = DdFlags.ResolveFlagSelection(
                new FlagSelection(new[] { " b", "a" }), new FlagSelectionReporter(logger));

            CollectionAssert.AreEqual(new[] { "a", "b" }, selection.Tags);
            logger.DidNotReceive().Log(Arg.Any<DdLogLevel>(), Arg.Any<string>());
        }
    }
}
