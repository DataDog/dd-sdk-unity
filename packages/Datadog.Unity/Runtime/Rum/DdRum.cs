// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;

namespace Datadog.Unity.Rum
{
    public enum RumErrorSource
    {
        Source,
        Network,
        WebView,
        Console,
        Custom,
    }

    public enum RumHttpMethod
    {
        Get,
    }

    public enum RumResourceType
    {
        Image,
    }

    public interface IDdRum
    {
        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null);

        public void StopView(string key, Dictionary<string, object> attributes = null);

        public void AddTiming(string name);

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null);

        public void StartResourceLoading(string key, RumHttpMethod httpMethod, string url,
            Dictionary<string, object> attributes = null);

        public void StopResourceLoading(string key, RumResourceType kind, int? statusCode = null, int? size = null,
            Dictionary<string, object> attributes = null);

        public void StopResourceLoading(string key, Exception error, Dictionary<string, object> attributes = null);

        public void AddAttribute(string key, object value);

        public void RemoveAttribute(string key);

        public void AddFeatureFlagEvaluation(string key, object value);

        public void StopSession();
    }

    #region NoOp Implementation

    internal class DdNoOpRum : IDdRum
    {
        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null)
        {
        }

        public void StopView(string key, Dictionary<string, object> attributes = null)
        {
        }

        public void AddTiming(string name)
        {
        }

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
        }

        public void StartResourceLoading(string key, RumHttpMethod httpMethod, string url,
            Dictionary<string, object> attributes = null)
        {
            throw new NotImplementedException();
        }

        public void StopResourceLoading(string key, RumResourceType kind, int? statusCode = null, int? size = null,
            Dictionary<string, object> attributes = null)
        {
        }

        public void StopResourceLoading(string key, Exception error, Dictionary<string, object> attributes = null)
        {
        }

        public void AddAttribute(string key, object value)
        {
        }

        public void RemoveAttribute(string key)
        {
        }

        public void AddFeatureFlagEvaluation(string key, object value)
        {
        }

        public void StopSession()
        {
        }
    }

    #endregion
}
