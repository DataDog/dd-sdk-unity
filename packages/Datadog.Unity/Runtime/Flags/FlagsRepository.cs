// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using System.Text;

namespace Datadog.Unity.Flags
{
    /// <summary>
    /// Thread-safe repository for storing precomputed flag assignments and evaluation context.
    /// </summary>
    internal class FlagsRepository
    {
        private readonly object _lock = new();
        private FlagAssignments _assignments = new(new Dictionary<string, FlagAssignment>());
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
                if (key == null) return null;
                string lookupKey;
                try
                {
                    lookupKey = _assignments.Obfuscation?.Encode(key) ?? key;
                }
                catch (EncoderFallbackException)
                {
                    return null;
                }
                _assignments.Flags.TryGetValue(lookupKey, out var flag);
                return flag;
            }
        }

        /// <summary>
        /// Sets the flags and context atomically.
        /// </summary>
        public void SetFlagsAndContext(FlagsEvaluationContext context, Dictionary<string, FlagAssignment> flags)
        {
            SetFlagsAndContext(context, new FlagAssignments(flags ?? new Dictionary<string, FlagAssignment>()));
        }

        public void SetFlagsAndContext(FlagsEvaluationContext context, FlagAssignments assignments)
        {
            lock (_lock)
            {
                _context = context;
                _assignments = assignments;
            }
        }

        /// <summary>Keep fallback assignments only when the complete requested context matches.</summary>
        public void PrepareContext(FlagsEvaluationContext context)
        {
            lock (_lock)
            {
                if (ContextsMatch(_context, context)) return;
                _context = context;
                _assignments = new FlagAssignments(new Dictionary<string, FlagAssignment>());
            }
        }

        private static bool ContextsMatch(FlagsEvaluationContext left, FlagsEvaluationContext right)
        {
            if (left == null || right == null || left.TargetingKey != right.TargetingKey || left.Attributes.Count != right.Attributes.Count)
                return false;
            foreach (var attribute in left.Attributes)
                if (!right.Attributes.TryGetValue(attribute.Key, out var value) || value != attribute.Value)
                    return false;
            return true;
        }

        /// <summary>
        /// Returns true if any flags are cached.
        /// </summary>
        public bool HasFlags()
        {
            lock (_lock)
            {
                return _assignments.Flags.Count > 0;
            }
        }

        /// <summary>
        /// Returns a snapshot of all cached flags.
        /// </summary>
        public FlagAssignments GetFlagsSnapshot()
        {
            lock (_lock)
            {
                return _assignments;
            }
        }
    }
}
