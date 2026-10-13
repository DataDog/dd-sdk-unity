// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2026-Present Datadog, Inc.

#if DATADOG_INTEGRATION_SPLIT
using System;
using System.IO;
using System.Xml;
using NUnit.Framework.Interfaces;
using UnityEngine;
using UnityEngine.TestRunner;
using Datadog.Unity.Tests.Integration;

[assembly: TestRunCallback(typeof(IntegrationTestResultWriter))]

namespace Datadog.Unity.Tests.Integration
{
    public sealed class IntegrationTestResultWriter : ITestRunCallback
    {
        // Console output is captured by simctl without entering Datadog's Unity log handler.
        public void RunStarted(ITest testsToRun) => Console.WriteLine("[Integration tests] Starting test run");
        public void TestStarted(ITest test)
        {
            if (!test.IsSuite)
            {
                Console.WriteLine($"[Integration tests] START {test.FullName}");
            }
        }

        public void TestFinished(ITestResult result)
        {
            if (!result.Test.IsSuite)
            {
                Console.WriteLine($"[Integration tests] {result.ResultState.Status}: {result.Test.FullName}");
            }
        }

        public void RunFinished(ITestResult result)
        {
            var path = Path.Combine(Application.persistentDataPath, "nunit-integration-test-ios.xml");
            var temporary = path + ".tmp";
            using (var writer = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true }))
            {
                result.ToXml(true).WriteTo(writer);
            }
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(temporary, path);
            Console.WriteLine($"[Integration tests] Finished: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped");
            Console.WriteLine($"[Integration tests] NUnit results written to: {path}");
            Application.Quit(result.FailCount > 0 ? 1 : 0);
        }
    }
}
#endif
