// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections;
using System.Linq;
using Datadog.Unity;
using Datadog.Unity.Rum;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class FirstSceneBehavior : MonoBehaviour
{
    public TextMeshProUGUI statusText;
    public SpriteRenderer[] categorySprites;

    // Start is called before the first frame update
    void Start()
    {
        Debug.Assert(statusText != null, "statusText is not set in the inspector!");

        if (DemoManager.Instance.CategoryList == null || DemoManager.Instance.CategoryList.Count == 0)
        {
            FetchCategories();
        }
        else
        {
            StartCoroutine(PerformHomeActions());
        }
    }

    private void FetchCategories()
    {
        statusText.text = "Fetching categories from Shopist API...";

        var api = new ShopistApi();
        api.FetchCategories(onComplete: list =>
        {
            statusText.text = "Got categories: " + list.Count;
            DemoManager.Instance.CategoryList = list;
            StartCoroutine(PerformHomeActions());
        }, onError: s =>
        {
            statusText.text = "Got error: " + s;
        });
    }

    private IEnumerator PerformHomeActions()
    {
        // Only fetch images on first visit
        var demoManager = DemoManager.Instance;
        if (demoManager.CurrentCategoryTaps == 0)
        {
            yield return FetchCategoryImages();
            yield return new WaitForSeconds(3.0f);
        }

        if (!demoManager.DoneTappingCategories)
        {
            var categoryList = demoManager.CategoryList;
            var categoryIndex = demoManager.IncludeRandomness ? Random.Range(0, categoryList.Count) : 0;
            var category =  categoryList[categoryIndex];
            TapCategory(category);
        }
    }

    private IEnumerator FetchCategoryImages()
    {
        for (int i = 0; i < categorySprites.Length; ++i)
        {
            if (i >= DemoManager.Instance.CategoryList.Count)
            {
                break;
            }

            var category = DemoManager.Instance.CategoryList[i];
            var sprite = categorySprites[i];
            if (sprite == null)
            {
                break;
            }

            statusText.text = "Fetching Category image " + i;
            var textureWebRequest = UnityWebRequestTexture.GetTexture(category.cover);
            var tracked = new DatadogTrackedWebRequest(textureWebRequest);
            yield return tracked.SendWebRequest();

            if (textureWebRequest.result == UnityWebRequest.Result.Success)
            {
                var texture = DownloadHandlerTexture.GetContent(textureWebRequest);
                // Don't change the size of the sprite from current world size... mostly
                var spriteSize = sprite.gameObject.transform.localScale;
                var ppu = texture.width / spriteSize.x;
                sprite.sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: ppu);
            }
            else
            {
                statusText.text = "Error fetching image: " + textureWebRequest.error;
                Debug.LogError($"Error fetching image {textureWebRequest.url}: {textureWebRequest.error}");
            }
        }
    }

    private void TapCategory(Category category)
    {
        DemoManager.Instance.CurrentCategoryTaps++;
        DemoManager.Instance.CurrentCategory = category;
        DatadogSdk.Instance.Rum.AddAction(RumUserActionType.Tap, category.title);
        SceneManager.LoadScene("Scenes/CategoryScene");
    }
}
