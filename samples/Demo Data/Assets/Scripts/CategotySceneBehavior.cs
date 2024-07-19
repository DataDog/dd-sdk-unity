using System.Collections;
using System.Collections.Generic;
using Datadog.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CategotySceneBehavior : MonoBehaviour
{

    // Start is called before the first frame update
    void Start()
    {
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
        DemoManager.Instance.CurrentCategory = null;
        yield return SceneManager.LoadSceneAsync("Scenes/FirstScene");
    }

    private void FetchProducts(string categoryId)
    {
        var api = new ShopistApi();
        api.FetchProducts(categoryId, onComplete: products =>
        {
            var demoManager = DemoManager.Instance;
            demoManager.CategoryProducts[categoryId] = products;
        }, onError: s =>
        {
            Debug.LogError("Error fetching products: " + s);
        });
    }
}
