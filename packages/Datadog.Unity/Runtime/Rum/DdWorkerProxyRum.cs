// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Logs;
using Datadog.Unity.Worker;

namespace Datadog.Unity.Rum
{
    internal class DdWorkerProxyRum : IDdRum
    {
        private readonly DatadogWorker _worker;

        public DdWorkerProxyRum(DatadogWorker worker)
        {
            _worker = worker;
        }

        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null)
        {
            _worker.AddMessage(new DdRumProcessor.StartViewMessage(key, name, attributes));
        }

        public void StopView(string key, Dictionary<string, object> attributes = null)
        {
            _worker.AddMessage(new DdRumProcessor.StopViewMessage(key, attributes));
        }

        public void AddTiming(string name)
        {
            _worker.AddMessage(new DdRumProcessor.AddTimingMessage(name));
        }

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
            _worker.AddMessage(new DdRumProcessor.AddErrorMessage(error, source, attributes));
        }

        public void StartResourceLoading(string key, RumHttpMethod httpMethod, string url, Dictionary<string, object> attributes = null)
        {
            throw new NotImplementedException();
        }

        public void StopResourceLoading(string key, RumResourceType kind, int? statusCode = null, int? size = null,
            Dictionary<string, object> attributes = null)
        {
            throw new NotImplementedException();
        }

        public void StopResourceLoading(string key, Exception error, Dictionary<string, object> attributes = null)
        {
            throw new NotImplementedException();
        }

        public void AddAttribute(string key, object value)
        {
            _worker.AddMessage(new DdRumProcessor.AddAttributeMessage(key, value));
        }

        public void RemoveAttribute(string key)
        {
            _worker.AddMessage(new DdRumProcessor.RemoveAttributeMessage(key));
        }

        public void AddFeatureFlagEvaluation(string key, object value)
        {
            throw new NotImplementedException();
        }

        public void StopSession()
        {
            throw new NotImplementedException();
        }
    }
}
