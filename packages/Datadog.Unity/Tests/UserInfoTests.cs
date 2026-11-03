// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using Datadog.Unity.Core;
using Datadog.Unity.Worker;
using NSubstitute;
using NUnit.Framework;

namespace Datadog.Unity.Tests
{
    public class UserInfoTests
    {
        [TearDown]
        public void TearDown()
        {
            DatadogSdk.Shutdown();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClearUserInfoForwardsToPlatform(bool threaded)
        {
            var platform = Substitute.For<IDatadogPlatform>();
            DatadogWorker worker = threaded
                ? new ThreadedWorker(Substitute.For<IInternalLogger>())
                : new PassthroughWorker();
            platform.CreateWorker(Arg.Any<IInternalLogger>()).Returns(worker);
            DatadogSdk.InitWithPlatform(platform, new());

            DatadogSdk.Instance.ClearUserInfo();
            worker.Stop();

            platform.Received(1).ClearUserInfo();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClearUserInfoPreservesOrderBetweenUserChanges(bool threaded)
        {
            var platform = Substitute.For<IDatadogPlatform>();
            DatadogWorker worker = threaded
                ? new ThreadedWorker(Substitute.For<IInternalLogger>())
                : new PassthroughWorker();
            platform.CreateWorker(Arg.Any<IInternalLogger>()).Returns(worker);
            DatadogSdk.InitWithPlatform(platform, new());

            DatadogSdk.Instance.SetUserInfo("before");
            DatadogSdk.Instance.ClearUserInfo();
            DatadogSdk.Instance.ClearUserInfo();
            DatadogSdk.Instance.SetUserInfo("after");
            worker.Stop();

            Received.InOrder(() =>
            {
                platform.SetUserInfo("before", null, null, null);
                platform.ClearUserInfo();
                platform.ClearUserInfo();
                platform.SetUserInfo("after", null, null, null);
            });
        }
    }
}
