// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Datadog.Unity.Rum;
using UnityEngine;

namespace Datadog.Unity.Android
{
    public class DatadogAndroidRum : IDdRum
    {
        private readonly AndroidJavaObject _rum;

        public DatadogAndroidRum(AndroidJavaObject rum)
        {
            _rum = rum;
        }

        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null)
        {
            var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);
            name ??= key;
            _rum.Call("startView", key, name, javaAttributes);
        }

        public void StopView(string key, Dictionary<string, object> attributes = null)
        {
            var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);
            _rum.Call("stopView", key, javaAttributes);
        }

        public void AddUserAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            var javaActionType = GetUserActionType(type);
            var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);

            _rum.Call("addAction", javaActionType, name, javaAttributes);
        }

        public void StartUserAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            var javaActionType = GetUserActionType(type);
            var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);

            _rum.Call("startAction", javaActionType, name, javaAttributes);
        }

        public void StopUserAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            var javaActionType = GetUserActionType(type);
            var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);

            _rum.Call("stopAction", javaActionType, name, javaAttributes);
        }

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
            var message = error.Message;
            var processedStack = new DatadogExceptionProcessor().ProcessStackTrace(error);
            var stack = processedStack ?? error.StackTrace;
            var javaErrorSource = GetErrorSource(source);
            var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);

            _rum.Call("addErrorWithStacktrace", message, javaErrorSource, stack, javaAttributes);
        }

        public void StartResourceLoading(string key, RumHttpMethod httpMethod, string url, Dictionary<string, object> attributes = null)
        {
            var httpMethodString = GetHttpMethod(httpMethod);
            var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);

            _rum.Call("startResource", key, httpMethodString, url, javaAttributes);
        }

        public void StopResourceLoading(string key, RumResourceType kind, int? statusCode = null, long? size = null,
            Dictionary<string, object> attributes = null)
        {
            var javaResourceType = GetResourceType(kind);
            var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);

            // Because they're nullable, statusCode and size need to be converted to objects
            var javaStatusCode = statusCode != null ? new AndroidJavaObject("java.lang.Integer", (int)statusCode) : null;
            var javaSize = size != null ? new AndroidJavaObject("java.lang.Long", (long)size) : null;
            _rum.Call("stopResource", key, javaStatusCode, javaSize, javaResourceType, javaAttributes);
        }

        public void StopResourceLoading(string key, Exception error, Dictionary<string, object> attributes = null)
        {
            var message = error.Message;
            var errorType = error.GetType().ToString();
            var javaAttributes = DatadogAndroidHelpers.DictionaryToJavaMap(attributes);
            var errorSource = GetErrorSource(RumErrorSource.Network);

            _rum.Call("stopResourceWithError", key, null, message, errorSource,
                error.StackTrace ?? string.Empty, errorType, javaAttributes);
        }

        public void AddAttribute(string key, object value)
        {
            AndroidJavaObject javaValue = value == null ? null : DatadogAndroidHelpers.ObjectToJavaObject(value);
            _rum.Call("addAttribute", key, javaValue);
        }

        public void RemoveAttribute(string key)
        {
            _rum.Call("removeAttribute", key);
        }

        public void AddFeatureFlagEvaluation(string key, object value)
        {
            var javaValue = DatadogAndroidHelpers.ObjectToJavaObject(value);
            _rum.Call("addFeatureFlagEvaluation", key, javaValue);
        }

        public void StopSession()
        {
            _rum.Call("stopSession");
        }

        internal static AndroidJavaObject GetUserActionType(RumUserActionType action)
        {
            string actionName = action switch
            {
                RumUserActionType.Tap => "TAP",
                RumUserActionType.Scroll => "SCROLL",
                RumUserActionType.Swipe => "SWIPE",
                RumUserActionType.Custom => "CUSTOM",
                _ => "CUSTOM"
            };
            using var actionTypeClass = new AndroidJavaClass("com.datadog.android.rum.RumActionType");
            return actionTypeClass.GetStatic<AndroidJavaObject>(actionName);
        }

        internal static AndroidJavaObject GetErrorSource(RumErrorSource errorSource)
        {
            string errorSourceName = errorSource switch
            {
                RumErrorSource.Source => "SOURCE",
                RumErrorSource.Network => "NETWORK",
                RumErrorSource.WebView => "WEBVIEW",
                RumErrorSource.Console => "CONSOLE",
                RumErrorSource.Custom => "SOURCE",
                _ => "SOURCE"
            };
            using var errorSourceClass = new AndroidJavaClass("com.datadog.android.rum.RumErrorSource");
            return errorSourceClass.GetStatic<AndroidJavaObject>(errorSourceName);
        }

        internal static string GetHttpMethod(RumHttpMethod httpMethod)
        {
            string httpMethodName = httpMethod switch
            {
                RumHttpMethod.Post => "POST",
                RumHttpMethod.Get => "GET",
                RumHttpMethod.Head => "HEAD",
                RumHttpMethod.Put => "PUT",
                RumHttpMethod.Delete => "DELETE",
                RumHttpMethod.Patch => "PATCH",
                _ => "GET"
            };
            return httpMethodName;
        }

        internal static AndroidJavaObject GetResourceType(RumResourceType resourceType)
        {
            string resourceTypeName = resourceType switch
            {
                RumResourceType.Document => "DOCUMENT",
                RumResourceType.Image => "IMAGE",
                RumResourceType.Xhr => "XHR",
                RumResourceType.Beacon => "BEACON",
                RumResourceType.Css => "CSS",
                RumResourceType.Fetch => "FETCH",
                RumResourceType.Font => "FONT",
                RumResourceType.Js => "JS",
                RumResourceType.Media => "MEDIA",
                RumResourceType.Other => "OTHER",
                RumResourceType.Native => "NATIVE",
                _ => "OTHER"
            };
            using var rumResourceKindClass = new AndroidJavaClass("com.datadog.android.rum.RumResourceKind");
            return rumResourceKindClass.GetStatic<AndroidJavaObject>(resourceTypeName);
        }
    }
}
