// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using NSubstitute;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;

namespace Datadog.Unity.Flags.Tests
{
    public class NoopFlagsClientTests
    {
        private NoopFlagsClient _client;
        private IInternalLogger _logger;

        [SetUp]
        public void SetUp()
        {
            _logger = Substitute.For<IInternalLogger>();
            _client = new NoopFlagsClient("DISABLED", _logger);
        }

        [TearDown]
        public void TearDown()
        {
            _client.Dispose();
        }

        [Test]
        public void State_IsAlwaysDisabled()
        {
            Assert.AreEqual(FlagsClientState.Disabled, _client.State);
        }

        [Test]
        public void GetBooleanValue_ReturnsDefaultValue()
        {
            Assert.AreEqual(true, _client.GetBooleanValue("flag", true));
            Assert.AreEqual(false, _client.GetBooleanValue("flag", false));
        }

        [Test]
        public void GetStringValue_ReturnsDefaultValue()
        {
            Assert.AreEqual("default", _client.GetStringValue("flag", "default"));
        }

        [Test]
        public void GetIntegerValue_ReturnsDefaultValue()
        {
            Assert.AreEqual(42, _client.GetIntegerValue("flag", 42));
        }

        [Test]
        public void GetDoubleValue_ReturnsDefaultValue()
        {
            Assert.AreEqual(3.14, _client.GetDoubleValue("flag", 3.14));
        }

        [Test]
        public void GetDetails_ReturnsDefaultValueWithConstructorReason_AndNoError()
        {
            var details = _client.GetBooleanDetails("flag", false);

            Assert.AreEqual(false, details.Value);
            Assert.AreEqual("DISABLED", details.Reason);
            Assert.IsNull(details.Error);
        }

        [Test]
        public void GetDetails_ReturnsConstructorReason_ForDifferentReasons()
        {
            var errorClient = new NoopFlagsClient("ERROR", _logger);
            var details = errorClient.GetStringDetails("flag", "default");

            Assert.AreEqual("ERROR", details.Reason);
            Assert.IsNull(details.Error);
            errorClient.Dispose();
        }

        [Test]
        public void GetDetails_LogsDebugMessage_OnEachEvaluation()
        {
            _client.GetBooleanDetails("my-flag", false);

            _logger.Received(1).Log(
                DdLogLevel.Debug,
                Arg.Is<string>(s => s.Contains("my-flag") && s.Contains("DISABLED")));
        }

        [Test]
        public void SetEvaluationContext_InvokesCallbackWithFalse_Synchronously()
        {
            bool? result = null;
            _client.SetEvaluationContext(new FlagsEvaluationContext("user-1"), success => result = success);

            Assert.AreEqual(false, result, "onComplete should be called synchronously with false");
        }

        [Test]
        public void SetEvaluationContext_NullCallback_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                _client.SetEvaluationContext(new FlagsEvaluationContext("user-1")));
        }

        [Test]
        public void StateChanged_NeverFires()
        {
            var fired = false;
            _client.StateChanged += (_, _) => fired = true;

            _client.SetEvaluationContext(new FlagsEvaluationContext("user-1"));

            Assert.IsFalse(fired);
        }
    }
}
