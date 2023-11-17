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
            _mockRum.AddAction(Arg.Any<RumUserActionType>(), Arg.Any<string>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            // When
            rum.AddAction(RumUserActionType.Tap, "First Button", new ()
            {
                { "attribute_1", "my property" },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).AddAction(RumUserActionType.Tap, "First Button", Arg.Any<Dictionary<string, object>>());
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
            _mockRum.StartAction(Arg.Any<RumUserActionType>(), Arg.Any<string>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            // When
            rum.StartAction(RumUserActionType.Scroll, "Scroll List", new ()
            {
                { "attribute_1", "my property" },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).StartAction(RumUserActionType.Scroll, "Scroll List", Arg.Any<Dictionary<string, object>>());
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
            _mockRum.StopAction(Arg.Any<RumUserActionType>(), Arg.Any<string>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            // When
            rum.StopAction(RumUserActionType.Scroll, "Scroll List", new ()
            {
                { "attribute_1", "my property" },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).StopAction(RumUserActionType.Scroll, "Scroll List", Arg.Any<Dictionary<string, object>>());
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

        [Test]
        public void AddAttributeForwardsPropertiesToPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);

            // When
            rum.AddAttribute("test_attribute_2", 588182);
            Thread.Sleep(10);

            // Then
            _mockRum.Received(1).AddAttribute("test_attribute_2", 588182);
        }

        [Test]
        public void RemoveAttributeForwardsPropertiesToPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);

            // When
            rum.RemoveAttribute("test_attribute_2");
            Thread.Sleep(10);

            // Then
            _mockRum.Received(1).RemoveAttribute("test_attribute_2");
        }

        [Test]
        public void StartResourceLoadingForwardsToPlatformAddsTime()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);
            var date = new DateTime(2063, 4, 5, 12, 22, 10, DateTimeKind.Utc);
            Dictionary<string, object> capturedAttributes = null;
            _mockRum.StartResource(Arg.Any<string>(), Arg.Any<RumHttpMethod>(), Arg.Any<string>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            // When
            rum.StartResource("fake_resource", RumHttpMethod.Head, "http://fake/resource", new()
            {
                { "attribute_1", "my property" },
                { "int_attribute_3", 222 },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).StartResource("fake_resource", RumHttpMethod.Head, "http://fake/resource", Arg.Any<Dictionary<string, object>>());
            Assert.IsNotNull(capturedAttributes);
            Assert.AreEqual("my property", capturedAttributes["attribute_1"]);
            Assert.AreEqual(222, capturedAttributes["int_attribute_3"]);
            Assert.AreEqual(dateOffset.ToUnixTimeMilliseconds(), capturedAttributes["_dd.timestamp"]);
        }

        [Test]
        public void StopResourceLoadingForwardsToPlatformAddsTime()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);
            var date = new DateTime(2063, 4, 5, 12, 22, 10, DateTimeKind.Utc);
            Dictionary<string, object> capturedAttributes = null;
            _mockRum.StopResource(Arg.Any<string>(), Arg.Any<RumResourceType>(), Arg.Any<int?>(), Arg.Any<long?>(), Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            // When
            rum.StopResource("fake_resource", RumResourceType.Css, 200, 123999, new()
            {
                { "attribute_1", "my property" },
                { "int_attribute_3", 222 },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).StopResource("fake_resource", RumResourceType.Css, 200, 123999, Arg.Any<Dictionary<string, object>>());
            Assert.IsNotNull(capturedAttributes);
            Assert.AreEqual("my property", capturedAttributes["attribute_1"]);
            Assert.AreEqual(222, capturedAttributes["int_attribute_3"]);
            Assert.AreEqual(dateOffset.ToUnixTimeMilliseconds(), capturedAttributes["_dd.timestamp"]);
        }

        [Test]
        public void StopResourceLoadingWithErrorForwardsToPlatformAddsTime()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);
            var date = new DateTime(2063, 4, 5, 12, 22, 10, DateTimeKind.Utc);
            Dictionary<string, object> capturedAttributes = null;
            _mockRum.StopResourceWithError(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),Arg.Do<Dictionary<string, object>>(x => capturedAttributes = x));
            _mockDateProvider.Now.Returns(date);

            var exception = new Exception();

            // When
            rum.StopResource("fake_resource", exception, new()
            {
                { "attribute_1", "my property" },
                { "int_attribute_3", 222 },
            });
            Thread.Sleep(10);

            // Then
            var dateOffset = new DateTimeOffset(date);
            _mockRum.Received(1).StopResourceWithError("fake_resource", exception.GetType().ToString(), exception.Message,
                Arg.Any<Dictionary<string, object>>());
            Assert.IsNotNull(capturedAttributes);
            Assert.AreEqual("my property", capturedAttributes["attribute_1"]);
            Assert.AreEqual(222, capturedAttributes["int_attribute_3"]);
            Assert.AreEqual(dateOffset.ToUnixTimeMilliseconds(), capturedAttributes["_dd.timestamp"]);
        }

        [Test]
        public void AddFeatureFlagEvaluationForwardsPropertiesToPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);

            // When
            rum.AddFeatureFlagEvaluation("test_flag", "testing_value");
            Thread.Sleep(10);

            // Then
            _mockRum.Received(1).AddFeatureFlagEvaluation("test_flag", "testing_value");
        }

        [Test]
        public void StopSessionForwardsPropertiesToPlatform()
        {
            // Given
            var rum = new DdWorkerProxyRum(_worker, _mockDateProvider);

            // When
            rum.StopSession();
            Thread.Sleep(10);

            // Then
            _mockRum.Received(1).StopSession();
        }
    }
}
