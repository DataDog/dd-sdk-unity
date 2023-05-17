// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections;
using System.Collections.Generic;
using Datadog.Unity;
using Datadog.Unity.Logs;
using Datadog.Unity.Tests.Integration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

public class LoggingIntegrationTests
{
    [UnityTest]
    public IEnumerator LoggingIntegrationTestsSimplePasses()
    {
        var mockServerHelper = new MockServerHelper();

        yield return new MonoBehaviourTest<TestLoggingMonoBehavior>();

        var task = mockServerHelper.PollRequests(new TimeSpan(0, 0, 30), 1);
        yield return new WaitUntil(() => task.IsCompleted);
        var serverRequests = task.Result;

        Assert.AreEqual(1, serverRequests.Count);
    }

    public class TestLoggingMonoBehavior : MonoBehaviour, IMonoBehaviourTest
    {
        private bool _didSendLog = false;

        public bool IsTestFinished
        {
            get { return _didSendLog; }
        }

        public void Awake()
        {
            DatadogSdk.Instance.SetTrackingConsent(TrackingConsent.Granted);

            var loggingOptions = new DatadogLoggingOptions()
            {
                ServiceName = "logging.service",
            };
            var logger = DatadogSdk.Instance.CreateLogger(loggingOptions);
            logger.Info("Awake from test behavior");
            _didSendLog = true;
        }
    }
}
