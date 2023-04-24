// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Datadog.Unity;

public class TestBehavior : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        var datadog = new Datadog.Unity.Datadog();
        var logger = datadog.CreateLogger();
        logger.Log("Hello from Unity!");
    }

    // Update is called once per frame
    void Update()
    {

    }
}
