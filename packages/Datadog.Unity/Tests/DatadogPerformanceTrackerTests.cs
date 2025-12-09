// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Core;
using Datadog.Unity.Worker;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Datadog.Unity.Tests
{
    public class DatadogPerformanceTrackerInitializationTests
    {
        [TearDown]
        public void TearDown()
        {
            DatadogSdk.Shutdown();
        }

        [UnityTest]
        public IEnumerator DatadogInitCreatesPerformanceTracker_WhenVitalsUpdateFrequencyIsSet()
        {
            // Given an SDK config that includes RUM and has a nonzero VitalsUpdateFrequency
            var mockWorker = Substitute.For<DatadogWorker>();
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            mockPlatform.CreateWorker(Arg.Any<IInternalLogger>()).Returns(mockWorker);
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.RumEnabled = true;
            options.RumApplicationId = "some-application-id";
            options.VitalsUpdateFrequency = VitalsUpdateFrequency.Average;

            Assert.IsNull(GameObject.Find("DatadogPerformanceTracker"));

            // When we initialize the SDK
            DatadogSdk.InitWithPlatform(mockPlatform, options);
            mockWorker.Received(1).Start();

            // And wait a frame
            yield return null;

            // Then we should have exactly one DatadogPerformanceTracker in the scene
            var trackerObj = GameObject.Find("DatadogPerformanceTracker");
            Assert.IsNotNull(trackerObj);
            var tracker = trackerObj.GetComponent<DatadogPerformanceTracker>();
            Assert.IsNotNull(tracker);
        }

        [UnityTest]
        public IEnumerator DatadogInitDoesNotCreatePerformanceTracker_WhenVitalsUpdateFrequencyIsNone()
        {
            // Given an SDK config that includes RUM and has VitalsUpdateFrequency.None
            var mockWorker = Substitute.For<DatadogWorker>();
            var mockPlatform = Substitute.For<IDatadogPlatform>();
            mockPlatform.CreateWorker(Arg.Any<IInternalLogger>()).Returns(mockWorker);
            var options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            options.Enabled = true;
            options.RumEnabled = true;
            options.RumApplicationId = "some-application-id";
            options.VitalsUpdateFrequency = VitalsUpdateFrequency.None;

            Assert.IsNull(GameObject.Find("DatadogPerformanceTracker"));

            // When we initialize the SDK
            DatadogSdk.InitWithPlatform(mockPlatform, options);
            mockWorker.Received(1).Start();

            // And wait a frame
            yield return null;

            // Then we should have no DatadogPerformanceTracker in the scene
            var trackerObj = GameObject.Find("DatadogPerformanceTracker");
            Assert.IsNull(trackerObj);
        }
    }

    public class DatadogPerformanceTrackerTests
    {
        private GameObject _obj;
        private DatadogPerformanceTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _obj = new GameObject("DatadogPerformanceTracker");
            _tracker = _obj.AddComponent<DatadogPerformanceTracker>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_obj != null)
            {
                Object.Destroy(_obj);
            }
        }

        [UnityTest]
        public IEnumerator DatadogPerformanceTracker_ReportsSamplesDuringViews()
        {
            // Given a tracker initialized to report frame times every 0.10s
            var samples = new List<PerformanceSample>();
            Action<PerformanceSample> reportCallback = sample =>
            {
                samples.Add(sample);
            };
            float reportIntervalSeconds = 0.10f;
            _tracker.Init(reportCallback, reportIntervalSeconds);

            // When 0.25s elapses within a view
            yield return new WaitForSeconds(0.15f);
            _tracker.NotifyViewStarted();
            yield return new WaitForSeconds(0.25f);
            _tracker.NotifyViewStopped();
            yield return new WaitForSeconds(0.15f);

            // Then the tracker should report two samples
            Assert.That(samples.Count(), Is.EqualTo(2));
            Assert.That(samples[0].FrameTimeSeconds, Is.InRange(0.000001f, 0.1f));
        }

        [UnityTest]
        public IEnumerator DatadogPerformanceTracker_HandlesMultipleViewStartedCallsGracefully()
        {
            // Given a tracker initialized to report frame times every 0.10s
            var samples = new List<PerformanceSample>();
            Action<PerformanceSample> reportCallback = sample =>
            {
                samples.Add(sample);
            };
            float reportIntervalSeconds = 0.10f;
            _tracker.Init(reportCallback, reportIntervalSeconds);

            // When multiple StartView() calls occur without a matching StopView()
            _tracker.NotifyViewStarted();
            yield return new WaitForSeconds(0.15f); // 1 report total
            _tracker.NotifyViewStarted();
            yield return null;
            _tracker.NotifyViewStarted();
            yield return new WaitForSeconds(0.15f); // 2 reports total

            // Then the tracker should handle it by restarting the one and only report-callback timer for each view,
            // regardless of whether an explicit StopView() call occurred
            Assert.That(samples.Count(), Is.EqualTo(2));
            Assert.That(samples[0].FrameTimeSeconds, Is.InRange(0.000001f, 0.1f));
        }

        [UnityTest]
        public IEnumerator DatadogPerformanceTracker_ReportsOnStopView_IfNoSamplesPreviouslyReported()
        {
            // Given a tracker initialized to report frame times every 0.75s
            var samples = new List<PerformanceSample>();
            Action<PerformanceSample> reportCallback = sample =>
            {
                samples.Add(sample);
            };
            float reportIntervalSeconds = 0.75f;
            _tracker.Init(reportCallback, reportIntervalSeconds);

            // When a short-lived view ends before the first sample was sent
            _tracker.NotifyViewStarted();
            yield return new WaitForSeconds(0.1f);
            _tracker.NotifyViewStopped();

            // Then the tracker should report a sample on StopView so long as it's updated once during the view
            Assert.That(samples.Count(), Is.EqualTo(1));
            Assert.That(samples[0].FrameTimeSeconds, Is.InRange(0.000001f, 0.1f));
        }

        [Test]
        public void DatadogPerformanceTracker_DoesNotReport_IfNoUpdatesOccurred()
        {
            // Given a tracker initialized to report frame times every 0.5s
            var samples = new List<PerformanceSample>();
            Action<PerformanceSample> reportCallback = sample =>
            {
                samples.Add(sample);
            };
            float reportIntervalSeconds = 0.5f;
            _tracker.Init(reportCallback, reportIntervalSeconds);

            // When a view ends before any Update() calls can be processed on the tracker
            _tracker.NotifyViewStarted();
            _tracker.NotifyViewStopped();

            // Then the tracker should report no samples
            Assert.That(samples.Count(), Is.EqualTo(0));
        }
    }
}
