// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using Datadog.Demo.Unity.Api;
using Datadog.Unity;
using Datadog.Unity.Rum;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace Datadog.Demo.Unity
{
    public class CheckoutSceneBehavior : MonoBehaviour
    {
        public TextMeshProUGUI cartSummaryText;

        void Start()
        {
            Debug.Assert(cartSummaryText != null, "statusText is not set in the inspector!");

            StartCoroutine(PerformSceneActions());
        }

        private IEnumerator GoBack()
        {
            yield return new WaitForSeconds(2.0f);
            yield return SceneManager.LoadSceneAsync("Scenes/FirstScene");
        }

        private IEnumerator PerformSceneActions()
        {
            yield return new WaitForSeconds(2.0f);

            var demoManager = DemoManager.Instance;
            demoManager.Api.FakeFetchShippingAndTax(() =>
            {
                StartCoroutine(FinalizeCheckout());
            }, err =>
            {
                StartCoroutine(FinalizeCheckout());
            });
        }

        private IEnumerator FinalizeCheckout()
        {
            var breakdown = DemoManager.Instance.Cart.GenerateBreakdown();
            cartSummaryText.text = $"Total: {breakdown.Total:C2}\nTax: {breakdown.Tax:C2}\nShipping: {breakdown.Shipping:C2}";
            DatadogSdk.Instance.Rum?.AddError(
                new Exception("Tax&shipping cost cannot be calculated, default cost is used"),
                RumErrorSource.Source,
                attributes: new Dictionary<string, object>() {
                    {"tax", breakdown.Tax },
                    {"shipping", breakdown.Shipping }
                }
            );
            yield return new WaitForSeconds(Random.Range(1.0f, 2.0f));

            // Perform checkout then go back, but the demo is done no matter the outcome.
            bool shouldCheckout = true;
            DemoManager.Instance.IsDemoDone = true;
            if (DemoManager.Instance.IncludeRandomness)
            {
                shouldCheckout = Random.Range(0, 4) == 0;
            }

            if (shouldCheckout)
            {
                var checkout = Checkout.Random();
                DemoManager.Instance.Api.Checkout(checkout, null, (response) =>
                {
                    DatadogSdk.Instance.Rum?.AddAttribute("checkoutSuccess", true);
                    DemoManager.Instance.Cart.Clear();
                    StartCoroutine(GoBack());
                }, (err) =>
                {
                    DatadogSdk.Instance.Rum?.AddError(new Exception(err), RumErrorSource.Source);
                    StartCoroutine(GoBack());
                });
            }
            else
            {
                yield return GoBack();
            }
        }
    }
}
