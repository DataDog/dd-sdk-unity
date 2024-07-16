using System.Collections.Generic;
using Datadog.Unity;
using UnityEngine;

public class DemoManager : MonoBehaviour
{
    private static DemoManager _instance;
    public static DemoManager Instance {
        get
        {
            if (_instance != null) return _instance;

            _instance = (DemoManager) FindObjectOfType(typeof(DemoManager));
            if (_instance == null)
            {
                _instance = new GameObject("_DatadogManager").AddComponent<DemoManager>();
            }
            DontDestroyOnLoad(_instance.gameObject);

            return _instance;
        }
    }

    public List<Category> CategoryList;
    public Category CurrentCategory;
    public Dictionary<string, List<Product>> CategoryProducts = new Dictionary<string, List<Product>>();

    public void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this);
        }
        DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);
        DatadogSdk.Instance.SetSdkVerbosity(CoreLoggerLevel.Debug);
    }
}
