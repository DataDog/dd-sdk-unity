// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;

namespace Datadog.Unity
{
    internal readonly struct DeterministicSampler
    {
        private readonly bool _sampled;
        internal ulong Seed { get; }
        internal float SampleRate { get; }
        internal float RulePsr => SampleRate / 100.0f;

        internal DeterministicSampler(ulong seed, float sampleRate)
        {
            Seed = seed;
            SampleRate = NormalizeRate(sampleRate);
            if (SampleRate <= 0.0f)
            {
                _sampled = false;
            }
            else if (SampleRate >= 100.0f)
            {
                _sampled = true;
            }
            else
            {
                // Overflow and threshold operation order must match the native SDKs.
                var hash = unchecked(seed * 1111111111111111111UL);
                var threshold = (double)ulong.MaxValue * (double)SampleRate / 100.0;
                _sampled = (double)hash < threshold;
            }
        }

        internal bool Sample() => _sampled;

        internal DeterministicSampler CombineWith(float childRate)
        {
            return new DeterministicSampler(Seed, SampleRate * NormalizeRate(childRate) / 100.0f);
        }

        internal static float NormalizeRate(float rate)
        {
            return float.IsNaN(rate) ? 0.0f : Math.Clamp(rate, 0.0f, 100.0f);
        }
    }
}
