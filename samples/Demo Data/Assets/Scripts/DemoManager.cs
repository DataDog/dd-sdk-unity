// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Demo.Unity.Api;
using Datadog.Unity;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

public class DemoManager : MonoBehaviour
{
    private static DemoManager _instance;

    public static DemoManager Instance
    {
        get
        {
            if (_instance != null) return _instance;

            _instance = (DemoManager)FindObjectOfType(typeof(DemoManager));
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

    public bool IncludeErrors
    {
        get;
        set;
    } = true;

    public bool IncludeCrashes
    {
        get;
        set;
    } = false;

    // The number of category taps the demo expects
    private int DemoCategoryTaps { get; set; }

    // Whether the demo is done tapping categories
    public bool DoneTappingCategories
    {
        get { return CurrentCategoryTaps >= DemoCategoryTaps; }
    }

    // The number of times we've tapped categories
    public int CurrentCategoryTaps = 0;

    // The number of product taps the demo expects in each category
    private int DemoProductTaps { get; set; }

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

    public bool IsDemoDone = false;
    public Cart Cart = new Cart();
    public ShopistApi Api;

    public void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this);

            Api = gameObject.AddComponent<ShopistApi>();
            Api.IncludeRandomness = IncludeRandomness;
            Api.IncludeErrors = IncludeErrors;
        }

        DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);
        DatadogSdk.Instance.SetSdkVerbosity(CoreLoggerLevel.Debug);

        DemoCategoryTaps = IncludeRandomness ? Random.Range(1, 3) : 1;
        DemoProductTaps = IncludeRandomness ? Random.Range(2, 4) : 3;

        if (IncludeRandomness)
        {
            IncludeCrashes = Random.value < 0.2f;
        }
    }

    public void RandomlyCrash()
    {
        if (IncludeCrashes && Random.value < 0.4f)
        {
            var crashType = Random.Range(0, 2);
            switch (crashType)
            {
                case 0:
                    PerformNativeCCrash();
                    break;
                case 1:
                    PerformCppThrow();
                    break;
            }
        }
    }

    public void RandomlyHang()
    {
        if (IncludeRandomness && Random.value < 0.6f)
        {
            var hangTime = Random.Range(0.2f, 1.0f);
            Debug.Log($"Hanging for {hangTime} seconds...");
            var hangStartTime = DateTime.Now;
            while (true)
            {
                if(hangTime < (DateTime.Now - hangStartTime).TotalSeconds)
                {
                    break;
                }
            }
        }
    }

    // C / CPP exceptions
    [DllImport("__Internal", EntryPoint="perform_native_c_crash")]
    private static extern void PerformNativeCCrash();

    [DllImport("__Internal", EntryPoint="perform_cpp_throw")]
    private static extern void PerformCppThrow();

}
