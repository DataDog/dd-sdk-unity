// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Mime;
using Datadog.Unity;
using Datadog.Unity.Rum;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

public class ShopistApi : MonoBehaviour
{
    private const string ContentUrl = "https://shopist.io";
    private const string ApiUrl = "https://api.shopist.io";

    public bool IncludeRandomness { get; set; }
    public bool IncludeErrors { get; set; }
    public bool IncludeCrashes { get; set; }

    public void FetchCategories(Action<List<Category>> onComplete, Action<string> onError)
    {
        var request = DatadogTrackedWebRequest.Get($"{ContentUrl}/categories.json");
        var operation = request.SendWebRequest();
        operation.completed += _ =>
        {
            try
            {
                switch (request.result)
                {
                    case UnityWebRequest.Result.Success:
                        onComplete(JsonConvert.DeserializeObject<List<Category>>(request.downloadHandler.text));
                        break;
                    case UnityWebRequest.Result.ConnectionError:
                    case UnityWebRequest.Result.ProtocolError:
                        onError(request.error);
                        break;
                }
            }
            catch (Exception e)
            {
                onError(e.Message);
            }
        };
    }

    public void FetchProducts(string categoryId, Action<List<Product>> onComplete, Action<string> onError)
    {
        var request = DatadogTrackedWebRequest.Get($"{ContentUrl}/category_{categoryId}.json");
        var operation = request.SendWebRequest();
        operation.completed += _ =>
        {
            try
            {
                switch (request.result)
                {
                    case UnityWebRequest.Result.Success:
                        onComplete(JsonConvert.DeserializeObject<List<Product>>(request.downloadHandler.text));
                        break;
                    case UnityWebRequest.Result.ConnectionError:
                    case UnityWebRequest.Result.ProtocolError:
                        onError(request.error);
                        break;
                }
            }
            catch (Exception e)
            {
                onError(e.Message);
            }
        };
    }

    public void FakeFetchShippingAndTax(Action onComplete, Action<string> onError)
    {
        StartCoroutine(InnerFakeFetchShippingAndTax(onComplete, onError));
    }

    IEnumerator InnerFakeFetchShippingAndTax(Action onComplete, Action<string> onError)
    {
        // Random waits in this method are okay even when we're not including randomness in overall run
        yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));

        var getUrl = $"{ApiUrl}/shipping_tax.json";
        DatadogSdk.Instance.Rum?.StartResource(getUrl, RumHttpMethod.Get, getUrl);
        yield return new WaitForSeconds(Random.Range(0.05f, 0.1f));
        bool didError = false;
        if (IncludeErrors && IncludeRandomness)
        {
            if (Random.Range(0, 5) == 0)
            {
                didError = true;
                DatadogSdk.Instance.Rum?.StopResourceWithError(getUrl, "NetworkError",
                    "Shipping and taxes cannot be fetched from server");
                onError("FakeFetchShippingAndTax failed");
            }
        }

        if (!didError)
        {
            DatadogSdk.Instance.Rum?.StopResource(getUrl, RumResourceType.Native, 200, Random.Range(0, 3072) + 1024);
            onComplete();
        }
    }

    public void Checkout(Checkout payment, string couponCode, Action<PaymentResponse> onComplete,
        Action<string> onError)
    {
        var query = "";
        if (couponCode != null)
        {
            query = $"?coupon_code={couponCode}";
        }

        var checkoutJson = JsonConvert.SerializeObject(payment);
        var request = DatadogTrackedWebRequest.Post($"{ApiUrl}/checkout.json{query}", checkoutJson,
            MediaTypeNames.Application.Json);
        var operation = request.SendWebRequest();
        operation.completed += _ =>
        {
            try
            {
                switch (request.result)
                {
                    case UnityWebRequest.Result.Success:
                        onComplete(JsonConvert.DeserializeObject<PaymentResponse>(request.downloadHandler.text));
                        break;
                    case UnityWebRequest.Result.ConnectionError:
                    case UnityWebRequest.Result.ProtocolError:
                        onError(request.error);
                        break;
                }
            }
            catch (Exception e)
            {
                onError(e.Message);
            }
        };
    }
}
