// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections.Generic;
using Datadog.Unity.Flags;
using OpenFeature;
using UnityEngine;

/// <summary>
/// Example behavior demonstrating the Datadog Flags SDK via OpenFeature.
/// Attach this to a GameObject in your scene to see feature flags in action.
/// </summary>
public class FlagsBehavior : MonoBehaviour
{
    public void Start()
    {
        // 1. Enable the Flags feature (after Datadog SDK is already initialized).
        //    This registers the Datadog OpenFeature provider automatically.
        DdFlags.Enable(new FlagsConfiguration
        {
            TrackExposures = true,
            TrackEvaluations = true,
        });

        // 2. Create the default flags client
        DdFlags.CreateClient();

        // 3. Set the evaluation context — this fetches flag assignments from the server
        DdFlags.SetEvaluationContext(
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
                }
                else
                {
                    Debug.LogWarning("[Datadog Flags] Failed to load flags. Using defaults.");
                }

                EvaluateFlags();
            });
    }

    private async void EvaluateFlags()
    {
        // Evaluate flags through the standard OpenFeature API
        var client = Api.Instance.GetClient();

        var showNewUI = await client.GetBooleanValueAsync("show-new-ui", false);
        Debug.Log($"[OpenFeature] show-new-ui = {showNewUI}");

        var theme = await client.GetStringValueAsync("theme-color", "blue");
        Debug.Log($"[OpenFeature] theme-color = {theme}");

        var maxItems = await client.GetIntegerValueAsync("max-items-per-page", 25);
        Debug.Log($"[OpenFeature] max-items-per-page = {maxItems}");

        // Detailed evaluation with variant/reason info
        var details = await client.GetBooleanDetailsAsync("checkout-v2-enabled", false);
        Debug.Log($"[OpenFeature] checkout-v2-enabled: value={details.Value}, " +
                  $"variant={details.Variant ?? "n/a"}, reason={details.Reason ?? "n/a"}, " +
                  $"error={details.ErrorType}");
    }

    public void OnDestroy()
    {
        DdFlags.Shutdown();
    }
}
