// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using NUnit.Framework;

namespace Datadog.Unity.Rum.Tests
{
    public class DeterministicSamplerTests
    {
        [TestCaseSource(typeof(DeterministicSamplingTestCases), nameof(DeterministicSamplingTestCases.SeedCases))]
        public void MatchesNativeSeedVectors(ulong seed, float rate, bool expected)
        {
            var sampler = new DeterministicSampler(seed, rate);
            Assert.AreEqual(expected, sampler.Sample());
            Assert.AreEqual(expected, sampler.Sample());
        }

        [TestCaseSource(typeof(DeterministicSamplingTestCases), nameof(DeterministicSamplingTestCases.UuidCases))]
        public void MatchesNativeUuidVectors(string uuid, float rate, bool expected)
        {
            var session = RumSessionContext.FromSessionId(uuid);
            Assert.AreEqual(expected, new DeterministicSampler(session.SamplingSeed, rate).Sample());
        }

        [TestCase(0.0f, false)]
        [TestCase(-10.0f, false)]
        [TestCase(float.NaN, false)]
        [TestCase(float.NegativeInfinity, false)]
        [TestCase(100.0f, true)]
        [TestCase(150.0f, true)]
        [TestCase(float.PositiveInfinity, true)]
        public void BoundaryRatesApplyToAllSeeds(float rate, bool expected)
        {
            Assert.AreEqual(expected, new DeterministicSampler(0, rate).Sample());
            Assert.AreEqual(expected, new DeterministicSampler(ulong.MaxValue, rate).Sample());
        }

        [Test]
        public void OverflowWrapsInCheckedCaller()
        {
            checked
            {
                Assert.IsFalse(new DeterministicSampler(ulong.MaxValue, 50).Sample());
            }
        }

        [Test]
        public void HashAtThresholdIsRejected()
        {
            Assert.IsFalse(new DeterministicSampler(1UL << 63, 50).Sample());
        }

        [TestCase(50.0f, 20.0f, 10.0f)]
        [TestCase(50.0f, 50.0f, 25.0f)]
        [TestCase(100.0f, 20.0f, 20.0f)]
        [TestCase(0.0f, 100.0f, 0.0f)]
        [TestCase(50.0f, 100.0f, 50.0f)]
        [TestCase(50.0f, 0.0f, 0.0f)]
        [TestCase(25.0f, 150.0f, 25.0f)]
        public void CompositionUsesNormalizedProduct(float sessionRate, float traceRate, float expected)
        {
            var sampler = new DeterministicSampler(123, sessionRate).CombineWith(traceRate);
            Assert.AreEqual(expected, sampler.SampleRate);
            Assert.AreEqual(expected / 100.0f, sampler.RulePsr);
            Assert.AreEqual(123UL, sampler.Seed);
        }

        [Test]
        public void ChildCannotKeepAnIdentityRejectedByParent()
        {
            for (ulong seed = 0; seed < 1000; seed++)
            {
                var parent = new DeterministicSampler(seed, 50);
                if (parent.CombineWith(20).Sample())
                {
                    Assert.IsTrue(parent.Sample(), $"Seed {seed}");
                }
            }
        }
    }
}
