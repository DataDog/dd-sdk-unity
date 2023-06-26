// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

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

    public void OnCCrash()
    {
        PerformNativeCCrash();
    }

    public void OnCppThrow()
    {
        PerformCppThrow();
    }

    // C / CPP exceptions
    [DllImport("__Internal", EntryPoint="perform_native_c_crash")]
    private static extern void PerformNativeCCrash();

    [DllImport("__Internal", EntryPoint="perform_cpp_throw")]
    private static extern void PerformCppThrow();

#region iOS implementations
    #if PLATFORM_IOS
    // CrashHelper.swift
    [DllImport("__Internal")]
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
        Debug.Log("I don't know how to crash on this platform... 😞")
    }

    private static void PerformNativeThrow()
    {
        Debug.Log("I don't know how to throw on this platform... 😞")
    }
    #endif
#endregion
}
