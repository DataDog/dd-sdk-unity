// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
using Datadog.Unity.Worker;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Datadog.Unity.Rum.Tests
{
    public class RumContextLifecycleTests
    {
        [TearDown]
        public void TearDown() => DatadogSdk.Shutdown();

        private static IDatadogPlatform Platform(IRumContextProvider provider)
        {
            var platform = Substitute.For<IDatadogPlatform>();
            platform.CreateWorker(Arg.Any<IInternalLogger>()).Returns(new PassthroughWorker());
            platform.CreateLogger(Arg.Any<DatadogLoggingOptions>(), Arg.Any<DatadogWorker>()).Returns(new DdNoOpLogger());
            platform.InitRum(Arg.Any<DatadogConfigurationOptions>()).Returns(Substitute.For<IDdRumInternal>());
            platform.CreateRumContextProvider().Returns(provider);
            return platform;
        }

        private static DatadogConfigurationOptions Options(bool rumEnabled = true) => new()
        {
            RumEnabled = rumEnabled, RumApplicationId = "application", ForwardUnityLogs = false,
            AutomaticSceneTracking = false, VitalsUpdateFrequency = VitalsUpdateFrequency.None,
        };

        [Test]
        public void ProviderIsCreatedOnceAndDisposedOnShutdown()
        {
            var provider = Substitute.For<IRumContextProvider>();
            var platform = Platform(provider);
            DatadogSdk.InitWithPlatform(platform, Options());
            platform.Received(1).CreateRumContextProvider();
            DatadogSdk.Shutdown();
            provider.Received(1).Dispose();
            Assert.IsNull(DatadogSdk.Instance.CurrentRumSessionContext);
            Assert.IsNull(DatadogSdk.Instance.ResourceTrackingHelper);
        }

        [Test]
        public void DisabledRumDoesNotSubscribe()
        {
            var platform = Platform(Substitute.For<IRumContextProvider>());
            DatadogSdk.InitWithPlatform(platform, Options(false));
            platform.DidNotReceive().CreateRumContextProvider();
            Assert.IsNull(DatadogSdk.Instance.CurrentRumSessionContext);
        }

        [Test]
        public void ReinitializationUsesANewProvider()
        {
            using var first = new RumContextStore();
            using var second = new RumContextStore();
            first.Publish(RumContextStoreTests.FirstId);
            var platform = Platform(first);
            platform.CreateRumContextProvider().Returns(first, second);
            DatadogSdk.InitWithPlatform(platform, Options());
            Assert.AreEqual(RumContextStoreTests.FirstId, DatadogSdk.Instance.CurrentRumSessionContext.SessionId);
            DatadogSdk.Shutdown();
            DatadogSdk.InitWithPlatform(platform, Options());
            first.Publish(RumContextStoreTests.FirstId);
            Assert.IsNull(DatadogSdk.Instance.CurrentRumSessionContext);
            second.Publish(RumContextStoreTests.SecondId);
            Assert.AreEqual(RumContextStoreTests.SecondId, DatadogSdk.Instance.CurrentRumSessionContext.SessionId);
        }

        [Test]
        public void FailedInitializationDisposesProvider()
        {
            var provider = Substitute.For<IRumContextProvider>();
            var platform = Platform(provider);
            platform.InitRum(Arg.Any<DatadogConfigurationOptions>())
                .Throws(new InvalidOperationException("initialization failed"));
            DatadogSdk.InitWithPlatform(platform, Options());
            provider.Received(1).Dispose();
            Assert.IsNull(DatadogSdk.Instance.CurrentRumSessionContext);
        }
    }
}
