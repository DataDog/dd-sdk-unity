// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Tracks which exposures have been sent to avoid duplicate/over-frequent exposure events.
    /// Uses an LRU-like cache with a count limit, matching the iOS ExposureTracker pattern.
    /// Thread-safe via lock.
    /// </summary>
    internal class ExposureTracker
    {
        internal struct ExposureKey : IEquatable<ExposureKey>
        {
            public readonly string TargetingKey;
            public readonly string FlagKey;
            public readonly string AllocationKey;
            public readonly string VariationKey;

            public ExposureKey(string targetingKey, string flagKey, string allocationKey, string variationKey)
            {
                TargetingKey = targetingKey;
                FlagKey = flagKey;
                AllocationKey = allocationKey;
                VariationKey = variationKey;
            }

            public bool Equals(ExposureKey other)
            {
                return TargetingKey == other.TargetingKey
                    && FlagKey == other.FlagKey
                    && AllocationKey == other.AllocationKey
                    && VariationKey == other.VariationKey;
            }

            public override bool Equals(object obj)
            {
                return obj is ExposureKey other && Equals(other);
            }

            public override int GetHashCode()
                => HashCode.Combine(TargetingKey, FlagKey, AllocationKey, VariationKey);
        }

        public const int DefaultCountLimit = 1_000;

        private readonly object _lock = new();
        private readonly int _countLimit;
        private readonly LinkedList<ExposureKey> _order = new();
        private readonly Dictionary<ExposureKey, LinkedListNode<ExposureKey>> _cache = new();

        public ExposureTracker(int countLimit = DefaultCountLimit)
        {
            _countLimit = countLimit;
        }

        /// <summary>
        /// Returns true if the exposure has already been tracked.
        /// </summary>
        public bool Contains(ExposureKey exposure)
        {
            lock (_lock)
            {
                return _cache.ContainsKey(exposure);
            }
        }

        /// <summary>
        /// Atomically checks if the exposure is already tracked, and if not, inserts it.
        /// Returns true if the key was newly inserted (caller should send the exposure).
        /// Returns false if the key already existed (duplicate, no action needed).
        /// </summary>
        public bool TrackExposure(ExposureKey exposure)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(exposure))
                {
                    // Move to end (most recently used)
                    var node = _cache[exposure];
                    _order.Remove(node);
                    _order.AddLast(node);
                    return false;
                }

                // Evict oldest if at capacity
                if (_cache.Count >= _countLimit && _order.First != null)
                {
                    var oldest = _order.First;
                    _order.RemoveFirst();
                    _cache.Remove(oldest.Value);
                }

                var newNode = _order.AddLast(exposure);
                _cache[exposure] = newNode;
                return true;
            }
        }

        /// <summary>
        /// Gets the number of items in the cache.
        /// </summary>
        internal int Count
        {
            get
            {
                lock (_lock)
                {
                    return _cache.Count;
                }
            }
        }
    }
}
