// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using NUnit.Framework;

namespace Datadog.Unity.Rum.Tests
{
    public class DeterministicSamplerTests
    {
        // Shared dd-trace-go fixtures used by the native and Flutter SDK sampler tests.
        [TestCase(5577006791947779410UL, 94.0509f, true)]
        [TestCase(15352856648520921629UL, 43.7714f, true)]
        [TestCase(3916589616287113937UL, 68.6823f, true)]
        [TestCase(894385949183117216UL, 30.0912f, true)]
        [TestCase(12156940908066221323UL, 46.889f, true)]
        [TestCase(9828766684487745566UL, 15.6519f, false)]
        [TestCase(4751997750760398084UL, 81.364f, false)]
        [TestCase(11199607447739267382UL, 38.0657f, false)]
        [TestCase(6263450610539110790UL, 21.8553f, false)]
        [TestCase(1874068156324778273UL, 36.0871f, false)]
        public void MatchesNativeSeedVectors(ulong seed, float rate, bool expected)
        {
            var sampler = new DeterministicSampler(seed, rate);
            Assert.AreEqual(expected, sampler.Sample());
            Assert.AreEqual(expected, sampler.Sample());
        }

        [TestCase("11111111-2222-3333-4444-822107fcfd52", 94.050909f, true)]
        [TestCase("11111111-2222-3333-4444-4dc76695721d", 43.771419f, true)]
        [TestCase("11111111-2222-3333-4444-858149c6e2d1", 68.682307f, true)]
        [TestCase("11111111-2222-3333-4444-cb397916001e", 15.651925f, false)]
        [TestCase("11111111-2222-3333-4444-7f48392907a0", 30.091186f, true)]
        [TestCase("11111111-2222-3333-4444-7cc6f3875d04", 81.363996f, true)]
        [TestCase("11111111-2222-3333-4444-ffa2ba517936", 38.065719f, true)]
        [TestCase("11111111-2222-3333-4444-21587cb3ad0b", 46.888984f, false)]
        [TestCase("11111111-2222-3333-4444-768b7c4e0b68", 29.310186f, false)]
        [TestCase("11111111-2222-3333-4444-3f2525632186", 21.855305f, false)]
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
