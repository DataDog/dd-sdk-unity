// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using NUnit.Framework;

namespace Datadog.Unity.Tests
{
    public class DeterministicTraceSamplerTests
    {
        [Test]
        public void SampleByTraceId_WithZeroRate_ReturnsFalse()
        {
            // Given
            var sampler = new DeterministicTraceSampler(0.0f);

            // When
            var result = sampler.SampleByTraceId(12345ul);

            // Then
            Assert.IsFalse(result);
        }

        [Test]
        public void SampleByTraceId_WithFullRate_ReturnsTrue()
        {
            // Given
            var sampler = new DeterministicTraceSampler(1.0f);

            // When
            var result = sampler.SampleByTraceId(12345ul);

            // Then
            Assert.IsTrue(result);
        }

        [Test]
        public void SampleByTraceId_WithSameTraceId_ReturnsSameResult()
        {
            // Given
            var sampler = new DeterministicTraceSampler(0.5f);
            var traceId = 12345ul;

            // When
            var result1 = sampler.SampleByTraceId(traceId);
            var result2 = sampler.SampleByTraceId(traceId);

            // Then
            Assert.AreEqual(result1, result2);
        }

        [Test]
        public void SampleByTraceId_WithKnownValues_ReturnsExpectedResults()
        {
            // Test cases based on Knuth hashing behavior
            var sampler = new DeterministicTraceSampler(0.2f); // 20% sampling rate

            // These specific values should produce known results with the Knuth hasher
            Assert.IsTrue(sampler.SampleByTraceId(1ul));
            Assert.IsFalse(sampler.SampleByTraceId(ulong.MaxValue));
        }

        [Test]
        public void SampleByTraceId_ClampsSampleRate()
        {
            // Given - rates outside valid range
            var samplerNegative = new DeterministicTraceSampler(-0.5f);
            var samplerAboveOne = new DeterministicTraceSampler(1.5f);

            // When/Then - negative rate acts like 0
            Assert.IsFalse(samplerNegative.SampleByTraceId(12345ul));

            // When/Then - rate above 1 acts like 1
            Assert.IsTrue(samplerAboveOne.SampleByTraceId(12345ul));
        }

        [Test]
        public void SampleByTraceId_MatchesReferenceImplementation()
        {
            // Test cases generated using the dd-trace-go implementation
            // Reference: https://go.dev/play/p/CUrDJtze8E_e
            var testCases = new[]
            {
                (traceId: 5577006791947779410ul, sampleRate: 0.940509f, expected: true),
                (traceId: 15352856648520921629ul, sampleRate: 0.437714f, expected: true),
                (traceId: 3916589616287113937ul, sampleRate: 0.686823f, expected: true),
                (traceId: 894385949183117216ul, sampleRate: 0.300912f, expected: true),
                (traceId: 12156940908066221323ul, sampleRate: 0.46889f, expected: true),
                (traceId: 9828766684487745566ul, sampleRate: 0.156519f, expected: false),
                (traceId: 4751997750760398084ul, sampleRate: 0.81364f, expected: false),
                (traceId: 11199607447739267382ul, sampleRate: 0.380657f, expected: false),
                (traceId: 6263450610539110790ul, sampleRate: 0.218553f, expected: false),
                (traceId: 1874068156324778273ul, sampleRate: 0.360871f, expected: false),
            };

            foreach (var (traceId, sampleRate, expected) in testCases)
            {
                // Given
                var sampler = new DeterministicTraceSampler(sampleRate);

                // When
                var result = sampler.SampleByTraceId(traceId);

                // Then
                Assert.AreEqual(expected, result,
                    $"TraceId {traceId} with rate {sampleRate:P} should be {expected} but was {result}");
            }
        }
    }
}
