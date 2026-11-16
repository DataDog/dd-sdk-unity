// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System.Collections.Generic;
using Datadog.Unity.Core;
using Datadog.Unity.Logs;
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

        [Test]
        public void SetUserInfoRejectsInvalidIds([Values(null, "")] string id)
        {
            var platform = InitPlatform();
            DatadogSdk.Instance.SetUserInfo(id, "name", "email", new() { { "extra", "value" } });

            platform.DidNotReceive().SetUserInfo(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Dictionary<string, object>>());
            DatadogSdk.Instance.InternalLogger.Received(1).Log(
                DdLogLevel.Warn, "SetUserInfo requires a non-empty id. Ignoring.");
        }

        [Test]
        public void SetUserInfoNormalizesOptionalFields([Values(null, "", " ", "value")] string optionalValue)
        {
            var platform = InitPlatform();
            DatadogSdk.Instance.SetUserInfo(" ", optionalValue, optionalValue);

            var expected = string.IsNullOrEmpty(optionalValue) ? null : optionalValue;
            platform.Received(1).SetUserInfo(" ", expected, expected, null);
        }

        [Test]
        public void UserExtraInfoCannotOverrideReservedFields(
            [Values(false, true)] bool add,
            [Values("id", "name", "email", "anonymous_id")] string reservedKey)
        {
            var platform = InitPlatform();
            var extraInfo = new Dictionary<string, object> { { reservedKey, "spoofed" }, { "extra", "value" } };
            if (add)
            {
                DatadogSdk.Instance.AddUserExtraInfo(extraInfo);
            }
            else
            {
                DatadogSdk.Instance.SetUserInfo("user-id", "name", "email", extraInfo);
            }

            if (add)
            {
                platform.Received(1).AddUserExtraInfo(Arg.Is<Dictionary<string, object>>(
                    value => value.Count == 1 && (string)value["extra"] == "value"));
            }
            else
            {
                platform.Received(1).SetUserInfo("user-id", "name", "email", Arg.Is<Dictionary<string, object>>(
                    value => value.Count == 1 && (string)value["extra"] == "value"));
            }

            Assert.AreEqual("spoofed", extraInfo[reservedKey], "The caller's map must remain unchanged.");
            DatadogSdk.Instance.InternalLogger.Received(1).Log(
                DdLogLevel.Warn, $"User extra info key '{reservedKey}' is reserved. Ignoring.");
        }

        [Test]
        public void AddUserExtraInfoSkipsEmptyUpdates()
        {
            var platform = InitPlatform();
            DatadogSdk.Instance.AddUserExtraInfo(null);
            DatadogSdk.Instance.AddUserExtraInfo(new());
            DatadogSdk.Instance.AddUserExtraInfo(new() { { "id", "spoofed" } });

            platform.DidNotReceive().AddUserExtraInfo(Arg.Any<Dictionary<string, object>>());
        }

        [Test]
        public void ClearUserInfoForwardsToPlatform()
        {
            var platform = InitPlatform();

            DatadogSdk.Instance.ClearUserInfo();

            platform.Received(1).ClearUserInfo();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClearUserInfoPreservesOrderBetweenUserChanges(bool threaded)
        {
            DatadogWorker worker = threaded
                ? new ThreadedWorker(Substitute.For<IInternalLogger>())
                : new PassthroughWorker();
            var platform = InitPlatform(worker);

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

        private static IDatadogPlatform InitPlatform(DatadogWorker worker = null)
        {
            var platform = Substitute.For<IDatadogPlatform>();
            platform.CreateWorker(Arg.Any<IInternalLogger>()).Returns(worker ?? new PassthroughWorker());
            DatadogSdk.InitWithPlatform(platform, new());
            DatadogSdk.Instance.InternalLogger = Substitute.For<IInternalLogger>();
            return platform;
        }
    }
}
