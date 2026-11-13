// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Logs
{
    public class UserInfoIntegrationTests : IntegrationTestBase
    {
        [UnityTest]
        [Category("integration")]
        public IEnumerator UserInfoLifecycle()
        {
            var server = new MockServerHelper();
            yield return server.Clear();

            var sdk = DatadogSdk.Instance;
            sdk.SetTrackingConsent(TrackingConsent.Granted);
            sdk.ClearUserInfo();
            var logger = sdk.CreateLogger(new() { Name = "user-info", RemoteSampleRate = 100 });

            try
            {
                var expectedUsers = UserInfoTestHelpers.RecordUserChanges(message => logger.Info(message));
                var events = new List<LogDecoder>();
                yield return server.PollRequests(TimeSpan.FromSeconds(45), requests =>
                {
                    events = LogDecoder.LogsFromMockServer(requests)
                        .Where(value => expectedUsers.ContainsKey(value.Message)).ToList();
                    return expectedUsers.Keys.All(message => events.Any(value => value.Message == message));
                });

                var mismatches = new List<string>();
                foreach (var expected in expectedUsers)
                {
                    var matching = events.Where(value => value.Message == expected.Key).ToList();
                    if (matching.Count != 1)
                    {
                        mismatches.Add($"{expected.Key}: expected one event, got {matching.Count}.");
                        continue;
                    }

                    var mismatch = UserInfoTestHelpers.GetUserMismatch(expected.Value, JObject.FromObject(matching[0].UserExtraInfo), expected.Key);
                    if (mismatch != null)
                    {
                        mismatches.Add(mismatch);
                    }
                }

                Assert.IsEmpty(mismatches, string.Join("\n", mismatches));
            }
            finally
            {
                sdk.ClearUserInfo();
            }
        }
    }
}
