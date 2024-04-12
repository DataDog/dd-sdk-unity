// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity;
using Datadog.Unity.Rum;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UIBehavior : MonoBehaviour
{
    public void OnCrashButtonPressed()
    {
        PerformNativeCrash();
    }

    public void OnThrowButtonPressed()
    {
        PerformNativeThrow();
    }

    public void OnCSThrowButtonPressed()
    {
        try
        {
            throw new Exception("C# exception thrown!");
        }
        catch (Exception e)
        {
            DatadogSdk.Instance.Rum.AddError(e, RumErrorSource.Source);
        }
    }

    public void OnCCrash()
    {
        PerformNativeCCrash();
    }

    public void OnCppThrow()
    {
        PerformCppThrow();
    }

    public void AddScene()
    {
        SceneManager.LoadScene("Scenes/AddedScene", LoadSceneMode.Additive);
    }

    public void MoveScene()
    {
        SceneManager.LoadScene("Scenes/EmptyScene", LoadSceneMode.Single);
    }

    public void WebRequest()
    {
        StartCoroutine(DoWebRequest());
    }

    public void ErrorWebRequest()
    {
        StartCoroutine(DoErrorWebRequest());
    }

    public void BadWebRequest()
    {
        StartCoroutine(DoBadWebRequest());
    }

    public void ClearAllData()
    {
        DatadogSdk.Instance.ClearAllData();
    }

    private IEnumerator DoWebRequest()
    {
        var request = DatadogTrackedWebRequest.Get("https://httpbin.org/headers");
        yield return request.SendWebRequest();

        Debug.Log("Got result: " + request.downloadHandler.text);
    }

    private IEnumerator DoErrorWebRequest()
    {
        var request = DatadogTrackedWebRequest.Get("https://httpbin.org/status/500");
        yield return request.SendWebRequest();

        Debug.Log("Got result: " + request.downloadHandler.text);
    }

    private IEnumerator DoBadWebRequest()
    {
        var request = DatadogTrackedWebRequest.Get("https://127.9.12.123/test");
        yield return request.SendWebRequest();

        Debug.Log($"Bad request result was: {request.result}");
    }

    // C / CPP exceptions
    [DllImport("__Internal", EntryPoint="perform_native_c_crash")]
    private static extern void PerformNativeCCrash();

    [DllImport("__Internal", EntryPoint="perform_cpp_throw")]
    private static extern void PerformCppThrow();

#region iOS implementations
    #if PLATFORM_IOS
    // SwiftCrashHelper.swift
    [DllImport("__Internal", EntryPoint="PerformNativeSwiftCrash")]
    private static extern void PerformNativeCrash();

    [DllImport("__Internal", EntryPoint="throwObjectiveC")]
    private static extern void PerformNativeThrow();
    #endif
#endregion

#region Android implementation
    #if PLATFORM_ANDROID
    private static void PerformNativeCrash()
    {
        // Need a C function for this
    }

    private static void PerformNativeThrow()
    {
        using var jo = new AndroidJavaObject("datadog.unity.sample.KotlinCrashHelper");
        jo.CallStatic("throwException");
    }
    #endif
#endregion

#region NOOP implementation
    #if !PLATFORM_IOS && !PLATFORM_ANDROID
    private static void PerformNativeCrash()
    {
        // TODO: Add crashing behavior for non-mobile platforms
        Debug.Log("I don't know how to crash on this platform... 😞");
    }

    private static void PerformNativeThrow()
    {
        // TODO: Add throw behavior for non-mobile platforms
        Debug.Log("I don't know how to throw on this platform... 😞");
    }
    #endif
#endregion
}
