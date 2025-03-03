// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System.Collections;
using Datadog.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ProductSceneBehavior : MonoBehaviour
{
    public TextMeshProUGUI statusText;
    public Product product;

    // Start is called before the first frame update
    void Start()
    {
        Debug.Assert(statusText != null, "statusText is not set in the inspector!");

        var demoManager = DemoManager.Instance;
        if (demoManager.CurrentProduct != null)
        {
            product = demoManager.CurrentProduct;
            DatadogSdk.Instance.Rum.AddAttribute("category", demoManager.CurrentCategory.title);
            statusText.text = product.name;

            StartCoroutine(PerformSceneActions());
        }
        else
        {
            StartCoroutine(GoBack());
        }
    }

    private IEnumerator GoBack()
    {
        yield return new WaitForSeconds(2.0f);
        DatadogSdk.Instance.Rum.RemoveAttribute("category");
        DemoManager.Instance.CurrentProduct = null;
        yield return SceneManager.LoadSceneAsync("Scenes/CategoryScene");
    }

    private IEnumerator PerformSceneActions()
    {
        // Okay for this to be randomized even when we're using a deterministic e2e build.
        DemoManager.Instance.RandomlyHang();
        var waitTime = Random.Range(1.0f, 4.0f);
        yield return new WaitForSeconds(waitTime);
        if (product != null && !DemoManager.Instance.Cart.HasProduct(product))
        {
            bool shouldAdd = true;
            if (DemoManager.Instance.IncludeRandomness)
            {
                shouldAdd = Random.Range(0, 4) == 0;
            }

            if (shouldAdd)
            {
                DemoManager.Instance.Cart.AddItem(product, 1);
                DatadogSdk.Instance.Rum?.AddAttribute("hasPurchased", false);
            }
        }
        yield return GoBack();
    }
}
