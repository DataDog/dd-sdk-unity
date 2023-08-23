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

        public void AddUserAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            attributes ??= new Dictionary<string, object>();
            var jsonAttributes = JsonConvert.SerializeObject(attributes);

            DatadogRumBridge.DatadogRum_AddUserAction(type.ToString(), name, jsonAttributes);
        }

        public void StartUserAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            attributes ??= new Dictionary<string, object>();
            var jsonAttributes = JsonConvert.SerializeObject(attributes);

            DatadogRumBridge.DatadogRum_StartUserAction(type.ToString(), name, jsonAttributes);
        }

        public void StopUserAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            attributes ??= new Dictionary<string, object>();
            var jsonAttributes = JsonConvert.SerializeObject(attributes);

            DatadogRumBridge.DatadogRum_StopUserAction(type.ToString(), name, jsonAttributes);
        }

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
            attributes ??= new Dictionary<string, object>();
            var jsonAttributes = JsonConvert.SerializeObject(attributes);

            var errorType = error?.GetType()?.ToString();
            var errorMessage = error?.Message;
            var stackTrace = error?.StackTrace;

            DatadogRumBridge.DatadogRum_AddError(errorMessage, source.ToString(), errorType, stackTrace, jsonAttributes);
        }

        public void StartResourceLoading(string key, RumHttpMethod httpMethod, string url, Dictionary<string, object> attributes = null)
        {
            attributes ??= new Dictionary<string, object>();
            var jsonAttributes = JsonConvert.SerializeObject(attributes);

            DatadogRumBridge.DatadogRum_StartResourceLoading(key, httpMethod.ToString(), url, jsonAttributes);
        }

        public void StopResourceLoading(string key, RumResourceType kind, int? statusCode = null, long? size = null,
            Dictionary<string, object> attributes = null)
        {
            attributes ??= new Dictionary<string, object>();
            var jsonAttributes = JsonConvert.SerializeObject(attributes);

            // Note - using -1 as a special value to mean null, as sending optionals to C from C# is... difficult
            DatadogRumBridge.DatadogRum_StopResourceLoading(key, kind.ToString(), statusCode ?? -1, size ?? -1, jsonAttributes);
        }

        public void StopResourceLoading(string key, Exception error, Dictionary<string, object> attributes = null)
        {
            attributes ??= new Dictionary<string, object>();
            var jsonAttributes = JsonConvert.SerializeObject(attributes);

            var errorType = error?.GetType()?.ToString();
            var errorMessage = error?.Message;
            var stackTrace = error?.StackTrace;

            DatadogRumBridge.DatadogRum_StopResourceLoadingWithError(key, errorType, errorMessage, jsonAttributes);
        }

        public void AddAttribute(string key, object value)
        {
            var valueDict = new Dictionary<string, object>()
            {
                { "value", value },
            };
            var encodedValue = JsonConvert.SerializeObject(valueDict);
            DatadogRumBridge.DatadogRum_AddAttribute(key, encodedValue);
        }

        public void RemoveAttribute(string key)
        {
            DatadogRumBridge.DatadogRum_RemoveAttribute(key);
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

        [DllImport("__Internal")]
        public static extern void DatadogRum_AddUserAction(string type, string name, string attributes);

        [DllImport("__Internal")]
        public static extern void DatadogRum_StartUserAction(string type, string name, string attributes);

        [DllImport("__Internal")]
        public static extern void DatadogRum_StopUserAction(string type, string name, string attributes);

        [DllImport("__Internal")]
        public static extern void DatadogRum_AddError(string message, string source, string type, string stack, string attributes);

        [DllImport("__Internal")]
        public static extern void DatadogRum_AddAttribute(string key, string value);

        [DllImport("__Internal")]
        public static extern void DatadogRum_RemoveAttribute(string key);

        [DllImport("__Internal")]
        public static extern void DatadogRum_StartResourceLoading(string key, string httpMethod, string url, string attributes);

        [DllImport("__Internal")]
        public static extern void DatadogRum_StopResourceLoading(string key, string kind, int statusCode, long size, string attributes);

        [DllImport("__Internal")]
        public static extern void DatadogRum_StopResourceLoadingWithError(string key, string errorType,
            string errorMessage, string jsonAttributes);
    }
}
