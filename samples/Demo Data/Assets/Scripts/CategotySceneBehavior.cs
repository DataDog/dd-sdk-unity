// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections;
using System.Collections.Generic;
using Datadog.Demo.Unity;
using Datadog.Unity;
using Datadog.Unity.Rum;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class CategotySceneBehavior : MonoBehaviour
{
    public TextMeshProUGUI statusText;

    private int _lastProductIndex = 0;

    // Start is called before the first frame update
    void Start()
    {
        Debug.Assert(statusText != null, "statusText is not set in the inspector!");

        var demoManager = DemoManager.Instance;
        if (demoManager.CurrentCategory != null)
        {
            DatadogSdk.Instance.Rum.AddAttribute("category", demoManager.CurrentCategory.title);
            FetchProducts(demoManager.CurrentCategory.id);
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
        DemoManager.Instance.CurrentProductTaps = 0;
        DemoManager.Instance.CurrentCategory = null;
        yield return SceneManager.LoadSceneAsync("Scenes/FirstScene");
    }

    private IEnumerator PerformSceneActions()
    {
        yield return FetchProductImages(0.0f, 4);
        StartCoroutine(FetchProductImages(0.3f, 2));

        var demoManager = DemoManager.Instance;
        if (demoManager.IncludeRandomness)
        {
            yield return ScrollRandomly();
        }
        else
        {
            yield return DeterministicScroll();
        }

        if (demoManager.DoneTappingProducts)
        {
            yield return GoBack();
        }
        else
        {
            if (!demoManager.CategoryProducts.TryGetValue(demoManager.CurrentCategory.id, out var products))
            {
                yield break;
            }

            var productTap = demoManager.IncludeRandomness ? Random.RandomRange(0, products.Count) : demoManager.CurrentProductTaps;
            var product = products[productTap];
            yield return TapProduct(product);
        }
    }

    private IEnumerator FetchProductImages(float delay, int number)
    {
        if (delay > 0.0f)
        {
            yield return new WaitForSeconds(delay);
        }

        var demoManager = DemoManager.Instance;
        if (!demoManager.CategoryProducts.TryGetValue(demoManager.CurrentCategory.id, out var products))
        {
            yield break;
        }

        for (int i = 0; i < number; ++i)
        {
            var index = _lastProductIndex + i;
            if (index >= products.Count)
            {
                break;
            }

            var product = products[index];
            var textureWebRequest = UnityWebRequestTexture.GetTexture(product.cover);
            var tracked = new DatadogTrackedWebRequest(textureWebRequest);
            yield return tracked.SendWebRequest();

            if (textureWebRequest.result == UnityWebRequest.Result.Success)
            {
                // Don't actually care
            }
            else
            {
                Debug.LogError($"Error fetching image {textureWebRequest.url}: {textureWebRequest.error}");
            }

            _lastProductIndex++;
        }
    }

    private IEnumerator DeterministicScroll()
    {
        statusText.text = "Scrolling Down for 3.1s...";
        yield return RumHelpers.FakeScroll(ScrollDirection.Down, 3.1f);
        statusText.text = "Scrolling Up for 1.2s...";
        yield return RumHelpers.FakeScroll(ScrollDirection.Up, 1.2f);
    }

    private IEnumerator ScrollRandomly()
    {
        var scrollTimes = Random.Range(0, 4);
        for (int i = 0; i < scrollTimes; ++i)
        {
            var direction = Random.Range(0, 2) == 0 ? ScrollDirection.Up : ScrollDirection.Down;
            var scrollTime = Random.Range(3.2f, 5.0f);
            statusText.text = $"Scroll {i + 1} of {scrollTimes} - {direction} for {scrollTime:F1}s...";
            yield return RumHelpers.FakeScroll(direction, scrollTime);

            var waitTime = Random.Range(1.0f, 4.0f);
            statusText.text = $"Wait for {waitTime:F1}s...";
            yield return new WaitForSeconds(waitTime);
        }
    }

    private IEnumerator TapProduct(Product product)
    {
        statusText.text = $"Tapping product {product.name}...";
        DemoManager.Instance.CurrentProductTaps++;
        DemoManager.Instance.CurrentProduct = product;
        DatadogSdk.Instance.Rum.AddAction(RumUserActionType.Tap, $"Product {product.name}");

        yield return SceneManager.LoadSceneAsync("ProductScene");
    }

    private void FetchProducts(string categoryId)
    {
        statusText.text = "Fetching products from Shopist API...";
        var api = new ShopistApi();
        api.FetchProducts(categoryId, onComplete: products =>
        {
            var demoManager = DemoManager.Instance;
            demoManager.CategoryProducts[categoryId] = products;

            StartCoroutine(PerformSceneActions());
        }, onError: s =>
        {
            statusText.text = $"Error fetching products: {s}";
            Debug.LogError("Error fetching products: " + s);
        });
    }
}
