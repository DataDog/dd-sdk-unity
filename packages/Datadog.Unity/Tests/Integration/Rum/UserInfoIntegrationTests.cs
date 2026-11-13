// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
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
            var viewKey = $"UserInfo-{Guid.NewGuid():N}";
            sdk.Rum.StartView(viewKey, viewKey);

            try
            {
                var expectedUsers = UserInfoTestHelpers.RecordUserChanges(message =>
                    sdk.Rum.AddError(new ErrorInfo("UserInfoTest", message), RumErrorSource.Source));
                var events = new List<RumErrorEventDecoder>();
                yield return server.PollRequests(TimeSpan.FromSeconds(45), requests =>
                {
                    events = RumDecoderHelpers.RumEventsFromMockServer(requests).OfType<RumErrorEventDecoder>()
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

                    var mismatch = UserInfoTestHelpers.GetUserMismatch(expected.Value, matching[0].UserAttributes, expected.Key);
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
#if !UNITY_WEBGL
                sdk.Rum.StopView(viewKey);
#endif
            }
        }
    }
}
