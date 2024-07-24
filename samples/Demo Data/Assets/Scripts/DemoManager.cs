// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

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

    public bool IncludeRandomness
    {
        get
        {
#if DATADOG_E2E_NONRANDOM
            return false;
#else
            return true;
#endif
        }
    }

    // The number of category taps the demo expects
    public int DemoCategoryTaps
    {
        get;
        private set;
    }

    // Whether the demo is done tapping categories
    public bool DoneTappingCategories
    {
        get { return CurrentCategoryTaps >= DemoCategoryTaps; }
    }

    // The number of times we've tapped categories
    public int CurrentCategoryTaps = 0;

    // The number of product taps the demo expects in each category
    public int DemoProductTaps
    {
        get;
        private set;
    }

    // The number of times we've tapped products
    public int CurrentProductTaps = 0;

    // Whether the demo is done tapping products
    public bool DoneTappingProducts
    {
        get { return CurrentProductTaps >= DemoProductTaps; }
    }

    public List<Category> CategoryList;
    public Category CurrentCategory;
    public Dictionary<string, List<Product>> CategoryProducts = new Dictionary<string, List<Product>>();
    public Product CurrentProduct;

    public void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this);
        }
        DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);
        DatadogSdk.Instance.SetSdkVerbosity(CoreLoggerLevel.Debug);

        DemoCategoryTaps = IncludeRandomness ? Random.Range(1, 5) : 1;
        DemoProductTaps = IncludeRandomness ? Random.Range(2, 9) : 3;
    }
}
