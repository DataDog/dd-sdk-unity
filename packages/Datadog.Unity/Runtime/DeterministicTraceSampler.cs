// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;

namespace Datadog.Unity
{
    /// <summary>
    /// Implements deterministic sampling decisions based on request ID, using the same logic as the implementation in
    /// dd-go.
    /// </summary>
    internal class DeterministicTraceSampler
    {
        private const ulong KnuthFactor = 1111111111111111111;
        private const ulong MaxTraceID = ulong.MaxValue;

        private readonly float _sampleRate;
        private readonly ulong _threshold;

        /// <param name="sampleRate">Fraction of traces that should be sampled; in the range [0..1].</param>
        public DeterministicTraceSampler(float sampleRate)
        {
            _sampleRate = Math.Clamp(sampleRate, 0.0f, 1.0f);
            _threshold = (ulong)((float)MaxTraceID * _sampleRate);
        }

        /// <summary>
        /// Determines whether a trace with the given ID should be sampled.
        /// </summary>
        /// <param name="traceIdLow64">The lower 64 bits of the trace ID.</param>
        /// <returns>True if the trace with the given ID should be sampled.</returns>
        public bool SampleByTraceId(ulong traceIdLow64)
        {
            // Clamp to 1.0, always sampled
            if (_sampleRate >= 1.0f)
            {
                return true;
            }

            // Clamp to 0.0, never sampled
            if (_sampleRate <= 0.0f)
            {
                return false;
            }

            // In C#, ulong is always 64 bits, and integer overflow is handled by wrapping (i.e. we take the lower 64
            // bits of the result value; the rest are truncated): we rely on this behavior for our Knuth multiplicative
            // hash, so declare it unchecked to be explicit
            unchecked
            {
                // We multiply our input value by a large magic constant, which gives us a pseudo-random value (lhs)
                // that's distributed evenly across the range of uint64; and then we check it against a threshold
                // representing X% of the uint64 range, where X is our configured sample rate
                return traceIdLow64 * KnuthFactor < _threshold;
            }
        }
    }
}
