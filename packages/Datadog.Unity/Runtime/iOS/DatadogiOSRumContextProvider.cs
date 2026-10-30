// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Runtime.InteropServices;
using System.Text;
using Datadog.Unity.Rum;

namespace Datadog.Unity.iOS
{
    internal sealed class DatadogiOSRumContextProvider : IRumContextProvider
    {
        private readonly RumContextStore _store = new();
        private readonly Func<string> _readNativeSessionId = ReadNativeSessionId;

        public RumSessionContext Current => _store.ReadFrom(_readNativeSessionId);

        public void Dispose() => _store.Dispose();

        private static string ReadNativeSessionId()
        {
            var buffer = new byte[37];
            var count = DatadogRum_GetSessionId(buffer, buffer.Length);
            return count > 0 ? Encoding.UTF8.GetString(buffer, 0, count) : null;
        }

        [DllImport("__Internal")]
        private static extern int DatadogRum_GetSessionId([Out] byte[] buffer, int capacity);
    }
}
