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
        Post,
        Get,
        Head,
        Put,
        Delete,
        Patch,
    }

    public enum RumResourceType
    {
        Document,
        Image,
        Xhr,
        Beacon,
        Css,
        Fetch,
        Font,
        Js,
        Media,
        Other,
        Native,
    }

    public enum RumUserActionType
    {
        Tap,
        Scroll,
        Swipe,
        Custom,
    }

    public interface IDdRum
    {
        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null);

        public void StopView(string key, Dictionary<string, object> attributes = null);

        public void AddAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null);

        public void StartAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null);

        public void StopAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null);

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null);

        public void StartResource(string key, RumHttpMethod httpMethod, string url,
            Dictionary<string, object> attributes = null);

        public void StopResource(string key, RumResourceType kind, int? statusCode = null, long? size = null,
            Dictionary<string, object> attributes = null);

        public void StopResource(string key, Exception error, Dictionary<string, object> attributes = null);

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

        public void AddAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
        }

        public void StartAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
        }

        public void StopAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
        }

        public void AddError(Exception error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
        }

        public void StartResource(string key, RumHttpMethod httpMethod, string url,
            Dictionary<string, object> attributes = null)
        {
        }

        public void StopResource(string key, RumResourceType kind, int? statusCode = null, long? size = null,
            Dictionary<string, object> attributes = null)
        {
        }

        public void StopResource(string key, Exception error, Dictionary<string, object> attributes = null)
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
