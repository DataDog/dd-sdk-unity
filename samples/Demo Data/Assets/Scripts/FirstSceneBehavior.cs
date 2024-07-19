using System;
using System.Collections;
using System.Collections.Generic;
using Datadog.Unity;
using Datadog.Unity.Rum;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class FirstSceneBehavior : MonoBehaviour
{
    public TextMeshProUGUI statusText;

    // Start is called before the first frame update
    void Start()
    {
        Debug.Assert(statusText != null, "statusText is not set in the inspector!");

        FetchCategories();
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
        yield return new WaitForSeconds(3.0f);
        var categoryList = DemoManager.Instance.CategoryList;
        var randomCategory =  categoryList[Random.Range(0, categoryList.Count)];
        TapCategory(randomCategory);
    }

    private void TapCategory(Category category)
    {
        DemoManager.Instance.CurrentCategory = category;
        DatadogSdk.Instance.Rum.AddAction(RumUserActionType.Tap, category.title);
        SceneManager.LoadScene("Scenes/CategoryScene");
    }
}
