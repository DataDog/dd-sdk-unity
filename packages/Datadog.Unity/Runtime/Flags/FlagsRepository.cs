// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Thread-safe repository for storing precomputed flag assignments and evaluation context.
    /// </summary>
    internal class FlagsRepository
    {
        private readonly object _lock = new();
        private Dictionary<string, FlagAssignment> _flags = new();
        private FlagsEvaluationContext _context;

        /// <summary>
        /// Gets the current evaluation context.
        /// </summary>
        public FlagsEvaluationContext Context
        {
            get
            {
                lock (_lock)
                {
                    return _context;
                }
            }
        }

        /// <summary>
        /// Gets a precomputed flag by key. Returns null if not found.
        /// </summary>
        public FlagAssignment GetFlagAssignment(string key)
        {
            lock (_lock)
            {
                _flags.TryGetValue(key, out var flag);
                return flag;
            }
        }

        /// <summary>
        /// Sets the flags and context atomically.
        /// </summary>
        public void SetFlagsAndContext(FlagsEvaluationContext context, Dictionary<string, FlagAssignment> flags)
        {
            lock (_lock)
            {
                _context = context;
                _flags = flags ?? new Dictionary<string, FlagAssignment>();
            }
        }

        /// <summary>
        /// Returns true if any flags are cached.
        /// </summary>
        public bool HasFlags()
        {
            lock (_lock)
            {
                return _flags.Count > 0;
            }
        }

        /// <summary>
        /// Returns a snapshot of all cached flags.
        /// </summary>
        public Dictionary<string, FlagAssignment> GetFlagsSnapshot()
        {
            lock (_lock)
            {
                return new Dictionary<string, FlagAssignment>(_flags);
            }
        }
    }
}
