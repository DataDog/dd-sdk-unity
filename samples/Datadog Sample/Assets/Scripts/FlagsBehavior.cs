// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Datadog.Unity;
using Datadog.Unity.Flags;
using UnityEngine;

/// <summary>
/// Example behavior demonstrating the Datadog Flags SDK.
/// Attach this to a GameObject in your scene to see feature flags in action.
/// </summary>
public class FlagsBehavior : MonoBehaviour
{
    public void Start()
    {
        DontDestroyOnLoad(gameObject);

        // 1. Enable the Flags feature (after Datadog SDK is already initialized)
        DdFlags.Enable(new FlagsConfiguration
        {
            TrackExposures = true,
            TrackEvaluations = true,
            EvaluationFlushIntervalSeconds = 10.0f,
        });

        // 2. Create a flags client
        var client = DdFlags.Instance.CreateClient();

        // 3. Set the evaluation context with user/session information
        client.SetEvaluationContext(
            new FlagsEvaluationContext("user-12345", new Dictionary<string, object>
            {
                { "email", "demo@example.com" },
                { "plan", "premium" },
                { "country", "US" },
            }),
            onComplete: success =>
            {
                if (success)
                {
                    Debug.Log("[Datadog Flags] Flags loaded successfully!");
                    EvaluateFlags(client);
                }
                else
                {
                    Debug.LogWarning("[Datadog Flags] Failed to load flags. Using defaults.");
                    EvaluateFlags(client);
                }
            });
    }

    private void EvaluateFlags(FlagsClient client)
    {
        // Simple value accessors
        var showNewUI = client.GetBooleanValue("show-new-ui", false);
        Debug.Log($"[Datadog Flags] show-new-ui = {showNewUI}");

        var theme = client.GetStringValue("theme-color", "blue");
        Debug.Log($"[Datadog Flags] theme-color = {theme}");

        var maxItems = client.GetIntegerValue("max-items-per-page", 25);
        Debug.Log($"[Datadog Flags] max-items-per-page = {maxItems}");

        var discountRate = client.GetDoubleValue("discount-rate", 0.0);
        Debug.Log($"[Datadog Flags] discount-rate = {discountRate}");

        // Detailed evaluation with variant/reason info
        var details = client.GetBooleanDetails("checkout-v2-enabled", false);
        Debug.Log($"[Datadog Flags] checkout-v2-enabled: value={details.Value}, " +
                  $"variant={details.Variant ?? "n/a"}, reason={details.Reason ?? "n/a"}, " +
                  $"error={details.Error?.ToString() ?? "none"}");
    }

    public void OnDestroy()
    {
        DdFlags.Shutdown();
    }
}
