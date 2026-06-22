// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;

namespace Datadog.Unity.Flags.Tests
{
    /// <summary>
    /// An in-memory <see cref="IKeyValueStore"/> test double backed by a <see cref="Dictionary{K,V}"/>.
    /// Placed in the Tests assembly only — never ships to end users (RESEARCH.md Pitfall 5, T-03-01).
    /// The public <see cref="Store"/> field exposes the underlying dictionary for test assertions.
    /// </summary>
    internal class DictionaryKeyValueStore : IKeyValueStore
    {
        /// <summary>
        /// Exposes the underlying dictionary so test code can assert on keys and values directly.
        /// </summary>
        public readonly Dictionary<string, string> Store = new();

        public string GetString(string key, string defaultValue) =>
            Store.TryGetValue(key, out var v) ? v : defaultValue;

        public void SetString(string key, string value) => Store[key] = value;

        public void DeleteKey(string key) => Store.Remove(key);

        public void Save() { } // intentional no-op — no persistence layer to flush
    }
}
