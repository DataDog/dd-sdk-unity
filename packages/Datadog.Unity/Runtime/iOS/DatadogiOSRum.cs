// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity.Rum;
using Newtonsoft.Json;

namespace Datadog.Unity.iOS
{
    internal class DatadogiOSRum : IDdRum
    {
        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null)
        {
            attributes ??= new Dictionary<string, object>();
            var jsonAttributes = JsonConvert.SerializeObject(attributes);

            DatadogRumBridge.DatadogRum_StartView(key, name, jsonAttributes);
        }

        public void StopView(string key, Dictionary<string, object> attributes = null)
        {
            attributes ??= new Dictionary<string, object>();
            var jsonAttributes = JsonConvert.SerializeObject(attributes);

            DatadogRumBridge.DatadogRum_StopView(key, jsonAttributes);
        }

        public void AddTiming(string name)
        {
            throw new NotImplementedException();
        }

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
        }

        public void RemoveAttribute(string key)
        {
            throw new NotImplementedException();
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

    internal static class DatadogRumBridge
    {
        [DllImport("__Internal")]
        public static extern void DatadogRum_StartView(string key, string name, string attributes);

        [DllImport("__Internal")]
        public static extern void DatadogRum_StopView(string key, string attributes);
    }
}
