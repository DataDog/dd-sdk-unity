// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;

namespace Datadog.Unity
{
    internal class RateBasedSampler
    {
        private float _sampleRate;
        private Random _random = new Random();

        /// <param name="sampleRate">Sample rate should be between 0 and 1.</param>
        public RateBasedSampler(float sampleRate)
        {
            _sampleRate = Math.Clamp(sampleRate, 0.0f, 1.0f);
        }

        public bool Sample()
        {
            if (_sampleRate <= 0.0)
            {
                return false;
            }

            if (_sampleRate >= 1.0)
            {
                return true;
            }

            return _random.NextDouble() <= _sampleRate;
        }
    }
}
