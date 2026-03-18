// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using NUnit.Framework;

namespace Datadog.Unity.Flags.Tests
{
    public class ExposureTrackerTests
    {
        [Test]
        public void NewTrackerContainsNoEntries()
        {
            var tracker = new ExposureTracker();
            var key = new ExposureTracker.ExposureKey("user-1", "flag-1", "alloc-1", "variant-a");
            Assert.IsFalse(tracker.Contains(key));
        }

        [Test]
        public void TrackedExposureIsFound()
        {
            var tracker = new ExposureTracker();
            var key = new ExposureTracker.ExposureKey("user-1", "flag-1", "alloc-1", "variant-a");

            tracker.TrackExposure(key);

            Assert.IsTrue(tracker.Contains(key));
        }

        [Test]
        public void DifferentExposureIsNotFound()
        {
            var tracker = new ExposureTracker();
            var key1 = new ExposureTracker.ExposureKey("user-1", "flag-1", "alloc-1", "variant-a");
            var key2 = new ExposureTracker.ExposureKey("user-1", "flag-2", "alloc-1", "variant-a");

            tracker.TrackExposure(key1);

            Assert.IsFalse(tracker.Contains(key2));
        }

        [Test]
        public void CacheEvictsOldestWhenFull()
        {
            var tracker = new ExposureTracker(countLimit: 3);

            var key1 = new ExposureTracker.ExposureKey("user", "flag-1", "alloc", "var");
            var key2 = new ExposureTracker.ExposureKey("user", "flag-2", "alloc", "var");
            var key3 = new ExposureTracker.ExposureKey("user", "flag-3", "alloc", "var");
            var key4 = new ExposureTracker.ExposureKey("user", "flag-4", "alloc", "var");

            tracker.TrackExposure(key1);
            tracker.TrackExposure(key2);
            tracker.TrackExposure(key3);

            Assert.AreEqual(3, tracker.Count);
            Assert.IsTrue(tracker.Contains(key1));

            // Adding a 4th should evict the oldest (key1)
            tracker.TrackExposure(key4);

            Assert.AreEqual(3, tracker.Count);
            Assert.IsFalse(tracker.Contains(key1));
            Assert.IsTrue(tracker.Contains(key2));
            Assert.IsTrue(tracker.Contains(key3));
            Assert.IsTrue(tracker.Contains(key4));
        }

        [Test]
        public void SameExposureKeyDeduplicatesAllFourDimensions()
        {
            var tracker = new ExposureTracker();

            // Same targeting key, different flag
            var a = new ExposureTracker.ExposureKey("user-1", "flag-A", "alloc-1", "var-1");
            var b = new ExposureTracker.ExposureKey("user-1", "flag-B", "alloc-1", "var-1");

            // Same flag, different targeting key
            var c = new ExposureTracker.ExposureKey("user-2", "flag-A", "alloc-1", "var-1");

            // Same everything except allocation
            var d = new ExposureTracker.ExposureKey("user-1", "flag-A", "alloc-2", "var-1");

            // Same everything except variation
            var e = new ExposureTracker.ExposureKey("user-1", "flag-A", "alloc-1", "var-2");

            tracker.TrackExposure(a);

            Assert.IsTrue(tracker.Contains(a));
            Assert.IsFalse(tracker.Contains(b));
            Assert.IsFalse(tracker.Contains(c));
            Assert.IsFalse(tracker.Contains(d));
            Assert.IsFalse(tracker.Contains(e));
        }

        [Test]
        public void DuplicateTrackDoesNotIncrementCount()
        {
            var tracker = new ExposureTracker();
            var key = new ExposureTracker.ExposureKey("user-1", "flag-1", "alloc-1", "variant-a");

            tracker.TrackExposure(key);
            tracker.TrackExposure(key);
            tracker.TrackExposure(key);

            Assert.AreEqual(1, tracker.Count);
        }
    }
}
