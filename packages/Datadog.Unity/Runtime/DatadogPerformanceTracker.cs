// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using UnityEngine;

namespace Datadog.Unity
{
    /// <summary>
    /// A snapshot of Unity-specific performance metrics.
    /// </summary>
    internal readonly struct PerformanceSample
    {
        public readonly float FrameTimeSeconds;

        public PerformanceSample(float frameTimeSeconds)
        {
            FrameTimeSeconds = frameTimeSeconds;
        }
    }

    /// <summary>
    /// Script used by the Datadog SDK to monitor the performance of Unity's main update
    /// loop. This behavior will be added to scene by the Datadog SDK if needed; there is
    /// no need to place it manually.
    /// </summary>
    [AddComponentMenu("")]
    internal class DatadogPerformanceTracker : MonoBehaviour
    {
        private float _frameTimeSeconds;

        private int _updateCount;
        private int _updateCountAtLastReport;
        private Action<PerformanceSample> _reportCallback;
        private float _reportIntervalSeconds;

        public void Init(Action<PerformanceSample> reportCallback, float intervalSeconds)
        {
            _reportCallback = reportCallback;
            _reportIntervalSeconds = intervalSeconds;
        }

        private void Update()
        {
            // Cache the latest per-frame update time
            _frameTimeSeconds = Time.unscaledDeltaTime;

            // Increment our frame count
            _updateCount++;
        }

        public void NotifyViewStarted()
        {
            // Don't schedule any periodic callbacks unless we've been initialized with reporting state
            if (_reportCallback == null || _reportIntervalSeconds <= 0.0f)
            {
                return;
            }

            // Ensure that we only have one repeating invocation
            CancelInvoke(nameof(ReportSample));
            InvokeRepeating(nameof(ReportSample), _reportIntervalSeconds, _reportIntervalSeconds);
        }

        public void NotifyViewStopped()
        {
            // Stop periodic reporting of samples until we have an active view
            CancelInvoke(nameof(ReportSample));

            // If we've never reported any samples for this view, proc the reporting callback to send data gathered
            // from the current view, if any is available
            if (_updateCountAtLastReport == 0)
            {
                ReportSample();
            }
        }

        private void ReportSample()
        {
            // Only report data if we've received at least one Update() call in the current view since the last report
            if (_reportCallback == null || _updateCount == _updateCountAtLastReport)
            {
                return;
            }

            // Create a snapshot of our current performance metrics and report that data via our configured callback
            var sample = new PerformanceSample(_frameTimeSeconds);
            _reportCallback(sample);
            _updateCountAtLastReport = _updateCount;
        }
    }
}
