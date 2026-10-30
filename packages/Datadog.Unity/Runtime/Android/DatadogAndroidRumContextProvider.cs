// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Threading;
using Datadog.Unity.Rum;
using UnityEngine;
using UnityEngine.Scripting;

namespace Datadog.Unity.Android
{
    internal sealed class DatadogAndroidRumContextProvider : IRumContextProvider
    {
        private readonly RumContextStore _store = new();
        private readonly ContextUpdateReceiver _receiver;
        private AndroidJavaObject _sdk;

        internal DatadogAndroidRumContextProvider(AndroidJavaObject sdk)
        {
            _sdk = sdk;
            _receiver = new ContextUpdateReceiver(_store);
            try
            {
                _sdk.Call("setContextUpdateReceiver", _receiver);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public RumSessionContext Current => _store.Current;

        public void Dispose()
        {
            _store.Dispose();
            var sdk = Interlocked.Exchange(ref _sdk, null);
            if (sdk == null)
            {
                return;
            }

            try
            {
                sdk.Call("removeContextUpdateReceiver", _receiver);
            }
            finally
            {
                sdk.Dispose();
            }
        }

        [Preserve]
        private sealed class ContextUpdateReceiver : AndroidJavaProxy
        {
            private readonly RumContextStore _store;

            internal ContextUpdateReceiver(RumContextStore store)
                : base("com.datadog.android.api.feature.FeatureContextUpdateReceiver")
            {
                _store = store;
            }

            [Preserve]
            public void onContextUpdate(string featureName, AndroidJavaObject context)
            {
                if (featureName != "rum")
                {
                    return;
                }

                try
                {
                    using var active = context?.Call<AndroidJavaObject>("get", "session_active");
                    using var id = context?.Call<AndroidJavaObject>("get", "session_id");
                    _store.Publish(active != null && !active.Call<bool>("booleanValue")
                        ? null
                        : id?.Call<string>("toString"));
                }
                catch (Exception)
                {
                    // Exceptions must not escape a callback on the native context thread.
                    _store.Publish(null);
                }
            }
        }
    }
}
