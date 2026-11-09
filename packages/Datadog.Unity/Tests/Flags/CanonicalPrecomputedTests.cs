// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

#if UNITY_EDITOR
using System;
using System.Numerics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Datadog.Unity.Flags.Tests
{
    public class CanonicalPrecomputedTests
    {
        public static IEnumerable<TestCaseData> Fixtures()
        {
            var script = AssetDatabase.FindAssets("CanonicalPrecomputedTests t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath).Single();
            var root = Path.Combine(Path.GetDirectoryName(script), "Fixtures", "precomputed");
            var paths = Directory.GetFiles(Path.Combine(root, "cases"), "*.json", SearchOption.AllDirectories);
            if (paths.Length == 0) throw new InvalidDataException("Empty canonical precompute corpus");
            var source = JObject.Parse(File.ReadAllText(Path.Combine(root, "SOURCE.json")));
            foreach (var path in paths.OrderBy(path => path))
                yield return new TestCaseData(path).SetName($"Canonical/{source["commit"]}/{Path.GetFileNameWithoutExtension(path)}");
        }

        [TestCaseSource(nameof(Fixtures))]
        public void SatisfiesCanonicalContract(string path)
        {
            var text = File.ReadAllText(path);
            var fixture = JObject.Parse(text, new JsonLoadSettings { LineInfoHandling = LineInfoHandling.Load });
            var evaluations = fixture["evaluations"].Cast<JObject>().ToList();
            var executed = Applies(fixture) ? evaluations.Count(Applies) : 0;
            TestContext.WriteLine($"{path}: executed evaluations={executed}; excluded evaluations={evaluations.Count - executed}");
            if (!Applies(fixture)) Assert.Ignore("Canonical platform exclusion");
            var response = RawResponse(text, fixture);
            if (fixture["expectedResponseError"] != null)
            {
                Assert.AreEqual("INVALID_RESPONSE", (string)fixture["expectedResponseError"]);
                Assert.Throws<JsonSerializationException>(() => PrecomputeAssignmentsFetcher.ParseResponse(response));
                return;
            }
            var context = new FlagsEvaluationContext((string)fixture["context"]["targetingKey"],
                fixture["context"]["attributes"].ToObject<Dictionary<string, object>>());
            var repository = new FlagsRepository();
            repository.SetFlagsAndContext(context, PrecomputeAssignmentsFetcher.ParseResponse(response));
            var exposures = new List<JToken>();
            var events = new List<JToken>();
            using var aggregator = new EvaluationAggregator(batch => events.AddRange(batch.Select(JToken.FromObject)));
            using var client = new FlagsClient(repository, new ExposureTracker(), aggregator, null, null,
                true, true, exposure => exposures.Add(JToken.FromObject(exposure)), FlagsClientState.Ready);
            foreach (var evaluation in fixture["evaluations"].Cast<JObject>().Where(Applies))
            {
                switch ((string)evaluation["variationType"])
                {
                    case "boolean": Check<bool>(client, evaluation); break;
                    case "string": Check<string>(client, evaluation); break;
                    case "integer": Check<int>(client, evaluation); break;
                    case "float": Check<double>(client, evaluation); break;
                    case "object": Check<JObject>(client, evaluation); break;
                    default: Assert.Fail("Unsupported canonical getter"); break;
                }
            }
            aggregator.Flush();
            AssertEvents(exposures, fixture["expectations"], "exposures");
            AssertEvents(events, fixture["expectations"], "evaluationEvents");
        }

        internal static string RawResponse(string text, JObject fixture)
        {
            var location = (IJsonLineInfo)fixture["response"];
            var start = 0;
            for (var line = 1; line < location.LineNumber; line++)
                start = text.IndexOf('\n', start) + 1;
            start += location.LinePosition - 1;
            if (text[start] != '{') throw new InvalidDataException("Expected a response object");
            var depth = 0;
            var quoted = false;
            var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return text.Substring(start, i - start + 1);
            }
            throw new InvalidDataException("Incomplete response object");
        }

        [Test]
        public void PreservesOriginalNumericResponseText()
        {
            const string text = "{\"response\":{\"value\":9007199254740993.0,\"text\":\"}\"},\"evaluations\":[]}";
            Assert.AreEqual("{\"value\":9007199254740993.0,\"text\":\"}\"}", RawResponse(text, JObject.Parse(text)));
        }

        [Test]
        public void IntegrationPayloadRetainsAllAssignments()
        {
            var script = AssetDatabase.FindAssets("CanonicalPrecomputedTests t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath).Single();
            var path = Path.Combine(Path.GetDirectoryName(script), "..", "Integration", "Flags", "Resources", "PrecomputePayload.json");
            var assignments = PrecomputeAssignmentsFetcher.ParseResponse(File.ReadAllText(path));
            Assert.AreEqual(6, assignments.Count);
        }

        private static void Check<T>(FlagsClient client, JObject evaluation)
        {
            var details = client.GetDetails((string)evaluation["flag"], evaluation["defaultValue"].ToObject<T>());
            var actual = new JObject
            {
                ["value"] = details.Value == null ? JValue.CreateNull() : JToken.FromObject(details.Value),
                ["reason"] = details.Reason,
                ["variantKey"] = details.Variant,
                ["errorCode"] = details.Error switch
                {
                    null => null,
                    FlagEvaluationError.FlagNotFound => "FLAG_NOT_FOUND",
                    FlagEvaluationError.TypeMismatch => "TYPE_MISMATCH",
                    FlagEvaluationError.ProviderNotReady => "PROVIDER_NOT_READY",
                    _ => throw new InvalidDataException("Unknown evaluation error"),
                },
            };
            Assert.IsTrue(Matches(actual["value"], evaluation["expectedResult"]["value"])
                && Matches(evaluation["expectedResult"]["value"], actual["value"]), $"{evaluation["flag"]}: exact value {actual["value"]}, expected {evaluation["expectedResult"]["value"]}");
            foreach (var field in ((JObject)evaluation["expectedResult"]).Properties())
                Assert.IsTrue(Matches(actual[field.Name], field.Value),
                    $"{evaluation["flag"]}: {field.Name}: {actual[field.Name]} ({actual[field.Name]?.Type}), expected {field.Value} ({field.Value.Type})");
        }

        private static bool Applies(JObject value)
        {
            bool Contains(string field) => value[field]?.Any(item => (string)item["platform"] == "unity") == true;
            return !Contains("_skip") && (value["_include"] == null || Contains("_include"));
        }

        private static void AssertEvents(List<JToken> events, JToken expectations, string field)
        {
            var remaining = new List<JToken>(events);
            foreach (var matcher in (expectations[field] ?? new JArray()).Cast<JObject>().Where(Applies))
            {
                var fields = new JObject(matcher.Properties().Where(property => !property.Name.StartsWith("_")));
                for (var i = 0; i < (int)matcher["_count"]; i++)
                {
                    var index = remaining.FindIndex(item => Matches(item, fields));
                    Assert.GreaterOrEqual(index, 0, $"Missing event for {matcher}; actual: {new JArray(remaining)}");
                    remaining.RemoveAt(index);
                }
            }
            if ((bool?)expectations["noUnmatchedEvents"] == true) Assert.IsEmpty(remaining, $"Unmatched {field}");
        }

        private static bool Matches(JToken actual, JToken expected)
        {
            if (expected.Type == JTokenType.Null || expected is JValue { Value: null })
                return actual == null || actual is JValue { Value: null };
            if (expected is JObject fields)
                return actual is JObject && fields.Properties().All(field => Matches(actual[field.Name], field.Value));
            if (actual != null && (actual.Type == JTokenType.Integer || actual.Type == JTokenType.Float)
                && (expected.Type == JTokenType.Integer || expected.Type == JTokenType.Float))
            {
                BigInteger? Integer(JToken token) => token.Type == JTokenType.Integer
                    ? BigInteger.Parse(token.ToString())
                    : Math.Truncate(token.Value<double>()) == token.Value<double>()
                        ? new BigInteger(token.Value<double>()) : (BigInteger?)null;
                var actualInteger = Integer(actual);
                var expectedInteger = Integer(expected);
                return actualInteger.HasValue || expectedInteger.HasValue
                    ? actualInteger == expectedInteger
                    : actual.Value<double>() == expected.Value<double>();
            }
            return JToken.DeepEquals(actual, expected);
        }
    }
}

#endif
