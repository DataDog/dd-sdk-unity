// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using Datadog.Unity;
using Datadog.Unity.Logs;
using UnityEngine;

public class TestBehavior : MonoBehaviour
{
    // Start is called before the first frame update
    public void Start()
    {
        var logger = DatadogSdk.Instance.CreateLogger(new DatadogLoggingOptions()
        {
            SendNetworkInfo = true,
            DatadogReportingThreshold = DdLogLevel.Notice,
        });
        logger.Info("Hello from Unity!");
    }
}
