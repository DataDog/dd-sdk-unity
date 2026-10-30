// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Threading;

namespace Datadog.Unity.Rum
{
    internal interface IRumContextProvider : IDisposable
    {
        RumSessionContext Current { get; }
    }

    internal sealed class RumSessionContext
    {
        internal string SessionId { get; }
        internal ulong SamplingSeed { get; }

        private RumSessionContext(string sessionId, ulong seed)
        {
            SessionId = sessionId;
            SamplingSeed = seed;
        }

        internal static RumSessionContext FromSessionId(string sessionId)
        {
            if (!Guid.TryParse(sessionId, out var uuid) || uuid == Guid.Empty)
            {
                return null;
            }

            // Guid's node bytes retain their network order despite its mixed-endian prefix.
            var bytes = uuid.ToByteArray();
            ulong seed = 0;
            for (var i = 10; i < 16; i++)
            {
                seed = (seed << 8) | bytes[i];
            }

            return new RumSessionContext(uuid.ToString("D"), seed);
        }
    }

    internal sealed class RumContextStore : IRumContextProvider
    {
        private readonly object _writeLock = new();
        private RumSessionContext _current;
        private bool _disposed;

        public RumSessionContext Current => Volatile.Read(ref _current);

        internal RumSessionContext ReadFrom(Func<string> readSessionId)
        {
            lock (_writeLock)
            {
                if (_disposed)
                {
                    return null;
                }

                // Order the native snapshot read, publication, and return against other readers and disposal.
                UpdateCurrent(readSessionId());
                return _current;
            }
        }

        internal void Publish(string sessionId)
        {
            lock (_writeLock)
            {
                if (_disposed)
                {
                    return;
                }

                UpdateCurrent(sessionId);
            }
        }

        private void UpdateCurrent(string sessionId)
        {
            if (_current != null && _current.SessionId == sessionId)
            {
                return;
            }

            Volatile.Write(ref _current, RumSessionContext.FromSessionId(sessionId));
        }

        public void Dispose()
        {
            lock (_writeLock)
            {
                _disposed = true;
                Volatile.Write(ref _current, null);
            }
        }
    }

    internal sealed class NoOpRumContextProvider : IRumContextProvider
    {
        internal static readonly NoOpRumContextProvider Instance = new();
        public RumSessionContext Current => null;
        public void Dispose() { }
    }
}
