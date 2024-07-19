// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Rum;
using Newtonsoft.Json;
using UnityEngine.Networking;

public class ShopistApi
{
    private const string ApiUrl = "https://shopist.io";

    public void FetchCategories(Action<List<Category>> onComplete, Action<string> onError)
    {
        var request = DatadogTrackedWebRequest.Get($"{ApiUrl}/categories.json");
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
        var request = DatadogTrackedWebRequest.Get($"{ApiUrl}/category_{categoryId}.json");
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
}
