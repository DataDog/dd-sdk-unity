// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Threading;
using Datadog.Unity.Worker;
using NSubstitute;
using NUnit.Framework;

namespace Datadog.Unity.Rum.Tests
{
    public class DdWorkerProxyRumTest
    {
        private DatadogWorker _worker;
        private DdRumProcessor _rumProcessor;
        private IDdRum _mockRum;
        private IDateProvider _mockDateProvider;

        [SetUp]
        public void SetUp()
        {
            _mockRum = Substitute.For<IDdRum>();
            _mockDateProvider = Substitute.For<IDateProvider>();

            _worker = new ();
            _rumProcessor = new (_mockRum);
            _worker.AddProcessor(DdRumProcessor.RumTargetName, _rumProcessor);
            _worker.Start();
        }

        [TearDown]
        public void TearDown()
        {
            _worker.Stop();
        }

        [Test]
        public void StartViewForwardsPropertiesAddsTimeForPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);
            var date = new DateTime(2015, 10, 21, 5, 35, 22, DateTimeKind.Utc);
            _mockDateProvider.Now.Returns(date);
            Dictionary<string, object> capturedAttributes = null;
            _mockRum.StartView(Arg.Any<string>(), Arg.Any<string>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));

            // When
            rum.StartView("view_key", "Test View", new ()
            {
                { "attribute_1", 245 },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).StartView("view_key", "Test View", Arg.Any<Dictionary<string, object>>());
            Assert.AreEqual(245, capturedAttributes["attribute_1"]);
            Assert.AreEqual(dateOffset.ToUnixTimeMilliseconds(), capturedAttributes["_dd.timestamp"]);
        }

        [Test]
        public void StopViewForwardsPropertiesAddsTimeForPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);
            var date = new DateTime(2015, 10, 21, 5, 35, 22, DateTimeKind.Utc);
            _mockDateProvider.Now.Returns(date);
            Dictionary<string, object> capturedAttributes = null;
            _mockRum.StopView(Arg.Any<string>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));

            // When
            rum.StopView("view_key", new ()
            {
                { "attribute_1", 245 },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).StopView("view_key", Arg.Any<Dictionary<string, object>>());
            Assert.IsNotNull(capturedAttributes);
            Assert.AreEqual(245, capturedAttributes["attribute_1"]);
            Assert.AreEqual(dateOffset.ToUnixTimeMilliseconds(), capturedAttributes["_dd.timestamp"]);
        }

        [Test]
        public void AddUserActionForwardsPropertiesAddsTimeForPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);
            var date = new DateTime(2063, 4, 5, 12, 22, 10, DateTimeKind.Utc);
            Dictionary<string, object> capturedAttributes = null;
            _mockRum.AddUserAction(Arg.Any<RumUserActionType>(), Arg.Any<string>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            // When
            rum.AddUserAction(RumUserActionType.Tap, "First Button", new ()
            {
                { "attribute_1", "my property" },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).AddUserAction(RumUserActionType.Tap, "First Button", Arg.Any<Dictionary<string, object>>());
            Assert.IsNotNull(capturedAttributes);
            Assert.AreEqual("my property", capturedAttributes["attribute_1"]);
            Assert.AreEqual(dateOffset.ToUnixTimeMilliseconds(), capturedAttributes["_dd.timestamp"]);
        }

        [Test]
        public void StartUserActionForwardsPropertiesAddsTimeForPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);
            var date = new DateTime(2063, 4, 5, 12, 22, 10, DateTimeKind.Utc);
            Dictionary<string, object> capturedAttributes = null;
            _mockRum.StartUserAction(Arg.Any<RumUserActionType>(), Arg.Any<string>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            // When
            rum.StartUserAction(RumUserActionType.Scroll, "Scroll List", new ()
            {
                { "attribute_1", "my property" },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).StartUserAction(RumUserActionType.Scroll, "Scroll List", Arg.Any<Dictionary<string, object>>());
            Assert.IsNotNull(capturedAttributes);
            Assert.AreEqual("my property", capturedAttributes["attribute_1"]);
            Assert.AreEqual(dateOffset.ToUnixTimeMilliseconds(), capturedAttributes["_dd.timestamp"]);
        }

        [Test]
        public void StopUserActionForwardsPropertiesAddsTimeForPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);
            var date = new DateTime(2063, 4, 5, 12, 22, 10, DateTimeKind.Utc);
            Dictionary<string, object> capturedAttributes = null;
            _mockRum.StopUserAction(Arg.Any<RumUserActionType>(), Arg.Any<string>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            // When
            rum.StopUserAction(RumUserActionType.Scroll, "Scroll List", new ()
            {
                { "attribute_1", "my property" },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).StopUserAction(RumUserActionType.Scroll, "Scroll List", Arg.Any<Dictionary<string, object>>());
            Assert.IsNotNull(capturedAttributes);
            Assert.AreEqual("my property", capturedAttributes["attribute_1"]);
            Assert.AreEqual(dateOffset.ToUnixTimeMilliseconds(), capturedAttributes["_dd.timestamp"]);
        }

        [Test]
        public void AddErrorForwardsPropertiesAddsTimeForPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);
            var date = new DateTime(2063, 4, 5, 12, 22, 10, DateTimeKind.Utc);
            Dictionary<string, object> capturedAttributes = null;
            _mockRum.AddError(Arg.Any<Exception>(), Arg.Any<RumErrorSource>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            var exception = new Exception();

            // When
            rum.AddError(exception, RumErrorSource.Custom, new()
            {
                { "attribute_1", "my property" },
                { "int_attribute_3", 222 },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).AddError(exception, RumErrorSource.Custom, Arg.Any<Dictionary<string, object>>());
            Assert.IsNotNull(capturedAttributes);
            Assert.AreEqual("my property", capturedAttributes["attribute_1"]);
            Assert.AreEqual(222, capturedAttributes["int_attribute_3"]);
            Assert.AreEqual(dateOffset.ToUnixTimeMilliseconds(), capturedAttributes["_dd.timestamp"]);
        }
    }
}
