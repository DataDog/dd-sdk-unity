// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace Datadog.Unity.Tests.Integration
{
    internal static class AsyncTestHelpers
    {
        // Browser SDK batches can wait 30 seconds before starting an upload.
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);

        public static async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
        {
            var duration = timeout ?? DefaultTimeout;
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.Elapsed >= duration)
                {
                    throw new TimeoutException($"Condition was not met after {clock.Elapsed.TotalSeconds:F1}s.");
                }

                // Yield through Unity's synchronization context; thread-pool timers are unavailable on WebGL.
                await Task.Yield();
            }
        }

        public static async Task Delay(TimeSpan duration)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < duration)
            {
                await Task.Yield();
            }
        }

        public static Task WaitForCompletion(AsyncOperation operation, TimeSpan? timeout = null)
        {
            // Wait for completion callbacks, including the tracked request wrapper, before disposing a request.
            var completed = false;
            operation.completed += _ => completed = true;
            return WaitUntil(() => completed, timeout);
        }

        public static Task WaitFor(CustomYieldInstruction instruction) => WaitUntil(() => !instruction.keepWaiting);
    }
}
