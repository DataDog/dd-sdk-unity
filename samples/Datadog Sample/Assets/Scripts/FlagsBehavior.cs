// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System.Collections;
using System.Collections.Generic;
using Datadog.Unity;
using Datadog.Unity.Flags;
using Datadog.Unity.Flags.OpenFeature;
using OpenFeature;
using UnityEngine;

/// <summary>
/// Example behavior demonstrating the Datadog Flags SDK via the OpenFeature API.
/// Attach this to a GameObject in your scene to see feature flags in action.
/// </summary>
public class FlagsBehavior : MonoBehaviour
{
    public void Start()
    {
        DontDestroyOnLoad(gameObject);
        StartCoroutine(InitFlags());
    }

    private IEnumerator InitFlags()
    {
        // 1. Enable the Flags feature (after Datadog SDK is already initialized)
        DdFlags.Enable(new FlagsConfiguration(evaluationFlushIntervalSeconds: 10.0f));

        // 2. Create a client and register the OpenFeature provider
        var client = DdFlags.Instance.CreateClient();
        var setProviderTask = Api.Instance.SetProviderAsync(new DatadogFeatureProvider(client));
        yield return new WaitUntil(() => setProviderTask.IsCompleted);

        // 3. Set the evaluation context with user/session information
        bool contextSet = false;
        client.SetEvaluationContext(
            new FlagsEvaluationContext("user-12345", new Dictionary<string, object>
            {
                { "email", "demo@example.com" },
                { "plan", "premium" },
                { "country", "US" },
            }),
            onComplete: success =>
            {
                if (!success)
                {
                    Debug.LogWarning("[Datadog Flags] Failed to load flags. Using defaults.");
                }
                contextSet = true;
            });

        yield return new WaitUntil(() => contextSet);

        // 4. Evaluate flags via the OpenFeature client
        StartCoroutine(EvaluateFlags());
    }

    private IEnumerator EvaluateFlags()
    {
        var ofClient = Api.Instance.GetClient();

        var showNewUITask = ofClient.GetBooleanValueAsync("show-new-ui", false);
        yield return new WaitUntil(() => showNewUITask.IsCompleted);
        Debug.Log($"[Datadog Flags] show-new-ui = {showNewUITask.Result}");

        var themeTask = ofClient.GetStringValueAsync("theme-color", "blue");
        yield return new WaitUntil(() => themeTask.IsCompleted);
        Debug.Log($"[Datadog Flags] theme-color = {themeTask.Result}");

        var maxItemsTask = ofClient.GetIntegerValueAsync("max-items-per-page", 25);
        yield return new WaitUntil(() => maxItemsTask.IsCompleted);
        Debug.Log($"[Datadog Flags] max-items-per-page = {maxItemsTask.Result}");

        var discountTask = ofClient.GetDoubleValueAsync("discount-rate", 0.0);
        yield return new WaitUntil(() => discountTask.IsCompleted);
        Debug.Log($"[Datadog Flags] discount-rate = {discountTask.Result}");

        var detailsTask = ofClient.GetBooleanDetailsAsync("checkout-v2-enabled", false);
        yield return new WaitUntil(() => detailsTask.IsCompleted);
        var details = detailsTask.Result;
        Debug.Log($"[Datadog Flags] checkout-v2-enabled: value={details.Value}, " +
                  $"variant={details.Variant ?? "n/a"}, reason={details.Reason ?? "n/a"}, " +
                  $"error={details.ErrorType}");
    }

    public void OnDestroy()
    {
        DdFlags.Shutdown();
    }
}
