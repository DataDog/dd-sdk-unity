// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Datadog.Unity;
using Datadog.Unity.Logs;
using TMPro;
using UnityEngine;

// Stress Test Behavior -- Last Results:
// iPhone 11 - 100 logs over 100 frames - 689ms or ~0.0689ms / log
// Samsung Galaxy A31 - 100 logs over 100 frames - 5831ms or ~0.5831ms / log
[SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "Unity members are public.")]
public class StressTestSceneBehavior : MonoBehaviour
{
    public int TargetFrameCount = 100;
    public int LogsPerFrame = 100;
    public TextMeshProUGUI ResultText;

    private const string AlphaNumeric = "abcdefghijklmnopqrztuvwxyzABCDEFGHIJKLMNOPQRZTUVWXYZ01234567890_";

    private bool _isRunningTest = false;
    private int _currentTestFrame = 0;
    private Stopwatch _stopwatch = new();

    // Update is called once per frame
    public void Update()
    {
        if (_isRunningTest)
        {
            for(int i = 0; i < LogsPerFrame; ++i)
            {
                GenerateRandomLog();
            }

            _currentTestFrame++;
            if (_currentTestFrame >= TargetFrameCount)
            {
                _isRunningTest = false;
                OnFinishTest();
            }
        }
    }

    public void OnStartTest()
    {
        _stopwatch.Reset();
        _isRunningTest = true;
        _currentTestFrame = 0;
        ResultText.text = $"Running test. (High Resolution Timer: {Stopwatch.IsHighResolution})";
    }

    private void OnFinishTest()
    {
        var milliseconds = _stopwatch.ElapsedMilliseconds;
        double timePerLog = ((double)milliseconds) / (TargetFrameCount * LogsPerFrame);
        var finalString = $"Sending {TargetFrameCount} logs over {LogsPerFrame} took {milliseconds}ms or {timePerLog}ms / log";
        ResultText.text = finalString;
    }

    private void GenerateRandomLog()
    {
        var randomLog = RandomString(60, 200);
        var randomAttr = RandomString(8, 20);
        var randomAttrValue = RandomString(10, 30);

        _stopwatch.Start();
        DatadogSdk.Instance.DefaultLogger.Log(DdLogLevel.Critical, randomLog, new() {
            { randomAttr, randomAttrValue },
        });
        _stopwatch.Stop();
    }

    private string RandomString(int minSize, int maxSize)
    {
        var stringSize = Random.Range(minSize, maxSize);
        var stringChars = new char[stringSize];
        for (int i = 0; i < stringChars.Length; i++)
        {
            stringChars[i] = AlphaNumeric[Random.Range(0, AlphaNumeric.Length)];
        }

        return new string(stringChars);
    }
}
