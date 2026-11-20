// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Datadog.Unity.Rum;
using Datadog.Unity.Tests.Integration.Rum.Decoders;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Datadog.Unity.Tests.Integration.Rum
{
    public class UserInfoIntegrationTests : IntegrationTestBase
    {
        [Test]
        [Category("integration")]
        public async Task UserInfoLifecycle()
        {
            var sdk = DatadogSdk.Instance;
            sdk.SetTrackingConsent(TrackingConsent.Granted);
            sdk.ClearUserInfo();
            var viewKey = $"UserInfo-{Guid.NewGuid():N}";
            sdk.Rum.StartView(viewKey, viewKey);

            try
            {
                var expectedUsers = UserInfoTestHelpers.RecordUserChanges(message =>
                    sdk.Rum.AddError(new ErrorInfo("UserInfoTest", message), RumErrorSource.Source));
                var received = await MockServer.WaitFor(
                    select: snapshot => snapshot.RumEvents.OfType<RumErrorEventDecoder>().Where(value => expectedUsers.ContainsKey(value.Message)).ToList(),
                    until: values => expectedUsers.Keys.All(message => values.Any(value => value.Message == message)));
                var events = received.Result;

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
