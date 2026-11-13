// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration
{
    internal static class UserInfoTestHelpers
    {
        internal static Dictionary<string, JObject> RecordUserChanges(Action<string> record)
        {
            var sdk = DatadogSdk.Instance;
            var prefix = $"UserInfo-{Guid.NewGuid():N}";
            var expectedUsers = new Dictionary<string, JObject>();

            void Record(string stage, JObject expectedUser)
            {
                var message = $"{prefix}-{stage}";
                expectedUsers.Add(message, expectedUser);
                record(message);
            }

            sdk.AddUserExtraInfo(new() { { "extra", "before-user" } });
            Record("extra-without-identity", new() { { "extra", "before-user" } });

            var extraInfo = new Dictionary<string, object>
            {
                { "extra", "initial" },
                { "kept", true },
                { "id", "spoofed" },
                { "name", "spoofed" },
                { "email", "spoofed" },
                { "anonymous_id", "spoofed" },
            };
            sdk.SetUserInfo("user-id", "User Name", "user@example.com", extraInfo);
            var initialUser = new JObject
            {
                { "id", "user-id" },
                { "name", "User Name" },
                { "email", "user@example.com" },
                { "extra", "initial" },
                { "kept", true },
            };
            Record("set", initialUser);

            sdk.SetUserInfo(null, "ignored", "ignored", new() { { "extra", "ignored" } });
            Record("null-id-ignored", initialUser);
            sdk.SetUserInfo(string.Empty, "ignored", "ignored", new() { { "extra", "ignored" } });
            Record("empty-id-ignored", initialUser);

            sdk.AddUserExtraInfo(new()
            {
                { "extra", "updated" },
                { "empty", string.Empty },
                { "count", 0 },
                { "enabled", false },
                { "id", "spoofed" },
                { "name", "spoofed" },
                { "email", "spoofed" },
                { "anonymous_id", "spoofed" },
            });
            var mergedUser = (JObject)initialUser.DeepClone();
            mergedUser["extra"] = "updated";
            mergedUser["empty"] = string.Empty;
            mergedUser["count"] = 0;
            mergedUser["enabled"] = false;
            Record("merge", mergedUser);

            sdk.AddUserExtraInfo(null);
            Record("null-extra-ignored", mergedUser);
            sdk.AddUserExtraInfo(new());
            Record("empty-extra-ignored", mergedUser);
            sdk.AddUserExtraInfo(new() { { "id", "spoofed" } });
            Record("reserved-extra-ignored", mergedUser);

            sdk.SetUserInfo("replacement-id", string.Empty, string.Empty, new() { { "empty", string.Empty } });
            Record("empty-optional-fields", new() { { "id", "replacement-id" }, { "empty", string.Empty } });
            sdk.SetUserInfo("id-only");
            Record("default-optional-fields", new() { { "id", "id-only" } });
            sdk.AddUserExtraInfo(new() { { "extra", "replace-me" } });
            sdk.SetUserInfo("empty-map", extraInfo: new());
            Record("empty-map-replaces-extra", new() { { "id", "empty-map" } });
            sdk.SetUserInfo(" ", " ", " ");
            Record("whitespace-preserved", new() { { "id", " " }, { "name", " " }, { "email", " " } });

            sdk.SetUserInfo("before-clear", "Name", "email@example.com", new() { { "extra", "clear-me" } });
            sdk.AddUserExtraInfo(new() { { "added", true } });
            Record("before-clear", new()
            {
                { "id", "before-clear" },
                { "name", "Name" },
                { "email", "email@example.com" },
                { "extra", "clear-me" },
                { "added", true },
            });
            sdk.ClearUserInfo();
            Record("cleared", new());
            sdk.ClearUserInfo();
            Record("cleared-again", new());
            sdk.AddUserExtraInfo(new() { { "extra", "after-clear" } });
            Record("extra-after-clear", new() { { "extra", "after-clear" } });
            sdk.SetUserInfo("signed-in-again");
            Record("set-after-clear", new() { { "id", "signed-in-again" } });
            return expectedUsers;
        }

        internal static string GetUserMismatch(JObject expected, JObject actual, string context)
        {
            actual = actual == null ? new JObject() : (JObject)actual.DeepClone();

            // Native SDKs may retain an automatically generated anonymous ID after clearing the user.
            var reservedAnonymousId = actual.Value<string>("anonymous_id") == "spoofed";
            actual.Remove("anonymous_id");
            if (!reservedAnonymousId && JToken.DeepEquals(expected, actual))
            {
                return null;
            }

            var reason = reservedAnonymousId ? " Reserved anonymous_id was overwritten." : string.Empty;
            return $"{context}: expected {expected}, got {actual}.{reason}";
        }
    }
}
