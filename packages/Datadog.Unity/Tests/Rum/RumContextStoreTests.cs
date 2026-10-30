// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using NUnit.Framework;
#if !UNITY_WEBGL
using System.Threading;
using System.Threading.Tasks;
#endif

namespace Datadog.Unity.Rum.Tests
{
    public class RumContextStoreTests
    {
        internal const string FirstId = "11111111-2222-3333-4444-000000000001";
        internal const string SecondId = "aaaaaaaa-bbbb-cccc-dddd-000000000002";

        [TestCase("aaaaaaaa-bbbb-cccc-dddd-0123456789ab")]
        [TestCase("AAAAAAAA-BBBB-CCCC-DDDD-0123456789AB")]
        [TestCase("11111111-2222-3333-4444-0123456789ab")]
        public void ExtractsUuidNodeInNetworkOrder(string id)
        {
            Assert.AreEqual(0x0123456789abUL, RumSessionContext.FromSessionId(id).SamplingSeed);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not-a-uuid")]
        [TestCase("00000000-0000-0000-0000-000000000000")]
        public void InvalidIdentityIsUnavailable(string id)
        {
            Assert.IsNull(RumSessionContext.FromSessionId(id));
        }

        [Test]
        public void ZeroNodeIsAValidSeedForNonzeroUuid()
        {
            var session = RumSessionContext.FromSessionId("11111111-2222-3333-4444-000000000000");
            Assert.IsNotNull(session);
            Assert.AreEqual(0UL, session.SamplingSeed);
        }

        [Test]
        public void PublishesReplacesAndClearsSnapshot()
        {
            using var store = new RumContextStore();
            Assert.IsNull(store.Current);
            store.Publish(FirstId);
            var first = store.Current;
            store.Publish(FirstId);
            Assert.AreSame(first, store.Current);
            store.Publish(SecondId);
            Assert.AreEqual(2UL, store.Current.SamplingSeed);
            Assert.AreEqual(1UL, first.SamplingSeed);
            store.Publish(null);
            Assert.IsNull(store.Current);
        }

        [Test]
        public void InvalidUpdateClearsPreviousSnapshot()
        {
            using var store = new RumContextStore();
            store.Publish(FirstId);
            store.Publish("invalid");
            Assert.IsNull(store.Current);
        }

        [Test]
        public void DisposedStoreIgnoresLateCallbacks()
        {
            var store = new RumContextStore();
            store.Publish(FirstId);
            store.Dispose();
            store.Publish(SecondId);
            store.Dispose();
            Assert.IsNull(store.Current);
            using var replacement = new RumContextStore();
            Assert.IsNull(replacement.Current);
        }

        [Test]
        public void PullReadsPublishReuseAndClearSnapshots()
        {
            using var store = new RumContextStore();
            var first = store.ReadFrom(() => FirstId);
            Assert.AreEqual(FirstId, first.SessionId);
            Assert.AreSame(first, store.ReadFrom(() => FirstId));
            Assert.AreEqual(SecondId, store.ReadFrom(() => SecondId).SessionId);
            Assert.IsNull(store.ReadFrom(() => null));
            Assert.IsNull(store.Current);
        }

        [Test]
        public void DisposedStoreDoesNotInvokePullReader()
        {
            using var store = new RumContextStore();
            store.Publish(FirstId);
            store.Dispose();
            var called = false;
            var current = store.ReadFrom(() => { called = true; return SecondId; });
            Assert.IsFalse(called);
            Assert.IsNull(current);
            Assert.IsNull(store.Current);
        }

#if !UNITY_WEBGL
        [Test]
        public void ConcurrentPullReadsKeepTheirOwnOrderedSnapshots()
        {
            using var store = new RumContextStore();
            using var firstReadStarted = new ManualResetEventSlim();
            using var releaseFirstRead = new ManualResetEventSlim();
            using var secondReaderStarted = new ManualResetEventSlim();
            using var secondReadStarted = new ManualResetEventSlim();
            var first = Task.Run(() => store.ReadFrom(() =>
            {
                firstReadStarted.Set();
                releaseFirstRead.Wait();
                return FirstId;
            }));
            Task<RumSessionContext> second = null;
            try
            {
                Assert.IsTrue(firstReadStarted.Wait(TimeSpan.FromSeconds(5)));
                second = Task.Run(() =>
                {
                    secondReaderStarted.Set();
                    return store.ReadFrom(() => { secondReadStarted.Set(); return SecondId; });
                });
                Assert.IsTrue(secondReaderStarted.Wait(TimeSpan.FromSeconds(5)));
                Assert.IsFalse(secondReadStarted.Wait(TimeSpan.FromMilliseconds(100)),
                    "The second native read must wait until the first snapshot is published and captured.");
            }
            finally
            {
                releaseFirstRead.Set();
                Assert.IsTrue(first.Wait(TimeSpan.FromSeconds(5)));
                if (second != null) Assert.IsTrue(second.Wait(TimeSpan.FromSeconds(5)));
            }
            Assert.AreEqual(FirstId, first.Result.SessionId);
            Assert.AreEqual(SecondId, second.Result.SessionId);
            Assert.AreEqual(SecondId, store.Current.SessionId);
        }

        [Test]
        public void ReadsDoNotWaitForProducerProgress()
        {
            using var store = new RumContextStore();
            using var ready = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            store.Publish(FirstId);
            var task = Task.Run(() => { ready.Set(); release.Wait(); store.Publish(SecondId); });
            try
            {
                Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(FirstId, store.Current.SessionId);
            }
            finally
            {
                release.Set();
                Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(5)));
            }
            Assert.AreEqual(SecondId, store.Current.SessionId);
        }

        [Test]
        public void ConcurrentReadersOnlySeeCompleteSnapshots()
        {
            using var store = new RumContextStore();
            store.Publish(FirstId);
            var writer = Task.Run(() =>
            {
                for (var i = 0; i < 1000; i++) store.Publish(i % 2 == 0 ? FirstId : SecondId);
            });
            do
            {
                var value = store.Current;
                Assert.AreEqual(value.SessionId == FirstId ? 1UL : 2UL, value.SamplingSeed);
            } while (!writer.IsCompleted);
            writer.GetAwaiter().GetResult();
        }
#endif
    }
}
