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
using UnityEngine;

namespace Datadog.Unity.Tests
{
    public class PlatformLifecycleTests
    {
        private DatadogConfigurationOptions _options;
        private IDatadogPlatform _platform;
        private DatadogWorker _worker;

        [SetUp]
        public void SetUp()
        {
            DatadogSdk.Shutdown();
            _options = ScriptableObject.CreateInstance<DatadogConfigurationOptions>();
            _platform = Substitute.For<IDatadogPlatform, IDisposable>();
            _worker = Substitute.For<DatadogWorker>();
            _platform.CreateWorker(Arg.Any<IInternalLogger>()).Returns(_worker);
            _platform.CreateLogger(Arg.Any<DatadogLoggingOptions>(), Arg.Any<DatadogWorker>()).Returns(new DdNoOpLogger());
        }

        [TearDown]
        public void TearDown()
        {
            DatadogSdk.Shutdown();
            UnityEngine.Object.DestroyImmediate(_options);
        }

        [Test]
        public void ShutdownStopsWorkerBeforeDisposingPlatform()
        {
            DatadogSdk.InitWithPlatform(_platform, _options);

            DatadogSdk.Shutdown();

            Received.InOrder(() =>
            {
                _worker.Stop();
                ((IDisposable)_platform).Dispose();
            });
        }

        [Test]
        public void RepeatedShutdownDisposesPlatformOnce()
        {
            DatadogSdk.InitWithPlatform(_platform, _options);

            DatadogSdk.Shutdown();
            DatadogSdk.Shutdown();

            ((IDisposable)_platform).Received(1).Dispose();
        }

        [Test]
        public void FailedInitializationDisposesPlatform()
        {
            _platform.CreateWorker(Arg.Any<IInternalLogger>()).Throws(new InvalidOperationException("initialization failed"));

            DatadogSdk.InitWithPlatform(_platform, _options);
            DatadogSdk.Shutdown();

            ((IDisposable)_platform).Received(1).Dispose();
        }
    }
}
