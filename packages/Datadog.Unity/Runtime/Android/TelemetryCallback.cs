// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.
using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace Datadog.Unity.Android
{
    public class TelemetryCallback : AndroidJavaProxy
    {
        //private AndroidJavaObject _javaObject;

        public TelemetryCallback() : base("com.datadog.android.event.EventMapper")
        {

        }

        public AndroidJavaObject map(AndroidJavaObject rumEvent)
        {
            var configuration = rumEvent.Call<AndroidJavaObject>("getTelemetry")?.Call<AndroidJavaObject>("getConfiguration");
            configuration?.Call("setUnityVersion", Application.unityVersion);

            return rumEvent;
        }
    }
}
