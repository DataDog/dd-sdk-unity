// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Datadog.Unity;
using Datadog.Unity.Core;
using Datadog.Unity.Rum;

namespace Datadog.Unity.Desktop
{
    internal class DatadogDesktopRum : IDdRumInternal
    {
        private readonly IntPtr _rum;
        private readonly IInternalLogger _logger;

        internal DatadogDesktopRum(IntPtr rum, IInternalLogger logger)
        {
            _rum = rum;
            _logger = logger;
        }

        public void StartView(string key, string name = null, Dictionary<string, object> attributes = null)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var attrs = GetAttrs(attributes);
            try
            {
                dd_rum_start_view_obj(_rum, key, name ?? key, ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public void StopView(string key, Dictionary<string, object> attributes = null)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var attrs = GetAttrs(attributes);
            try
            {
                dd_rum_stop_view_obj(_rum, key, ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public void AddAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var attrs = GetAttrs(attributes);
            try
            {
                dd_rum_add_action(_rum, MapActionType(type), name, ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public void StartAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var attrs = GetAttrs(attributes);
            try
            {
                dd_rum_start_action(_rum, MapActionType(type), name, ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public void StopAction(RumUserActionType type, string name, Dictionary<string, object> attributes = null)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var attrs = GetAttrs(attributes);
            try
            {
                dd_rum_stop_action(_rum, MapActionType(type), name, ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public void AddError(ErrorInfo error, RumErrorSource source, Dictionary<string, object> attributes = null)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var message = error?.Message;
            var type = error?.Type;
            var stack = error?.StackTrace ?? string.Empty;
            var attrs = GetAttrs(attributes);
            try
            {
                dd_rum_add_error(_rum, MapErrorSource(source), message, type, stack, ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public void StartResource(
            string key,
            RumHttpMethod httpMethod,
            string url,
            Dictionary<string, object> attributes = null)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var attrs = GetAttrs(attributes);
            try
            {
                dd_rum_start_resource(_rum, key, MapHttpMethod(httpMethod), url, ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public void StopResource(string key, RumResourceType kind, int? statusCode = null, long? size = null,
            Dictionary<string, object> attributes = null)
        {
            if (_rum == IntPtr.Zero) return;
            var attrs = GetAttrs(attributes);
            try
            {
                dd_rum_stop_resource(_rum, key, statusCode ?? 0, size ?? -1, MapResourceType(kind), ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public void StopResourceWithError(string key, string errorType, string errorMessage,
            Dictionary<string, object> attributes = null)
        {
            StopResourceWithError(key, new ErrorInfo(errorType, errorMessage), attributes);
        }

        public void StopResource(string key, Exception error, Dictionary<string, object> attributes = null)
        {
            StopResourceWithError(key, new ErrorInfo(error), attributes);
        }

        public void StopResourceWithError(string key, ErrorInfo error, Dictionary<string, object> attributes = null)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var message = error?.Message;
            var type = error?.Type;
            var stack = error?.StackTrace ?? string.Empty;
            var attrs = GetAttrs(attributes);
            try
            {
                dd_rum_stop_resource_with_error(_rum, key, message, type, stack, false, 0, ref attrs);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attrs);
            }
        }

        public void AddAttribute(string key, object value)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var attr = DdAttributes.MakeAttribute(value, _logger);
            try
            {
                dd_rum_add_attribute(_rum, key, ref attr);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attr);
            }
        }

        public void RemoveAttribute(string key)
        {
            if (_rum == IntPtr.Zero) return;
            dd_rum_remove_attribute(_rum, key);
        }

        public void AddFeatureFlagEvaluation(string key, object value)
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            var attr = DdAttributes.MakeAttribute(value?.ToString(), _logger);
            try
            {
                dd_rum_add_attribute(_rum, key, ref attr);
            }
            finally
            {
                DdAttributes.dd_attribute_free(ref attr);
            }
        }

        public void StopSession()
        {
            if (_rum == IntPtr.Zero)
            {
                return;
            }

            dd_rum_stop_session(_rum);
        }

        public void UpdateExternalRefreshRate(double frameTimeSeconds)
        {
            // Not supported by dd-sdk-cpp.
        }

        // === Helpers ===

        private DdAttribute GetAttrs(Dictionary<string, object> attributes)
        {
            return (attributes != null && attributes.Count > 0)
                ? DdAttributes.BuildAttributeObject(attributes, _logger)
                : DdAttributes.dd_attribute_null();
        }

        private static int MapActionType(RumUserActionType type) =>
            type switch
            {
                RumUserActionType.Tap => 0, // DD_RUM_ACTION_TYPE_TAP
                RumUserActionType.Scroll => 2, // DD_RUM_ACTION_TYPE_SCROLL
                RumUserActionType.Swipe => 3, // DD_RUM_ACTION_TYPE_SWIPE
                RumUserActionType.Custom => 4, // DD_RUM_ACTION_TYPE_CUSTOM
                _ => 4,
            };

        private static int MapHttpMethod(RumHttpMethod method) =>
            method switch
            {
                RumHttpMethod.Get => 0, // DD_RUM_RESOURCE_METHOD_GET
                RumHttpMethod.Head => 1, // DD_RUM_RESOURCE_METHOD_HEAD
                RumHttpMethod.Post => 2, // DD_RUM_RESOURCE_METHOD_POST
                RumHttpMethod.Put => 3, // DD_RUM_RESOURCE_METHOD_PUT
                RumHttpMethod.Delete => 4, // DD_RUM_RESOURCE_METHOD_DELETE
                RumHttpMethod.Patch => 8, // DD_RUM_RESOURCE_METHOD_PATCH
                _ => 0,
            };

        private static int MapResourceType(RumResourceType type) =>
            type switch
            {
                RumResourceType.Beacon => 1, // DD_RUM_RESOURCE_TYPE_BEACON
                RumResourceType.Fetch => 2, // DD_RUM_RESOURCE_TYPE_FETCH
                RumResourceType.Xhr => 3, // DD_RUM_RESOURCE_TYPE_XHR
                RumResourceType.Document => 4, // DD_RUM_RESOURCE_TYPE_DOCUMENT
                RumResourceType.Native => 5, // DD_RUM_RESOURCE_TYPE_NATIVE
                RumResourceType.Image => 6, // DD_RUM_RESOURCE_TYPE_IMAGE
                RumResourceType.Js => 7, // DD_RUM_RESOURCE_TYPE_JS
                RumResourceType.Font => 8, // DD_RUM_RESOURCE_TYPE_FONT
                RumResourceType.Css => 9, // DD_RUM_RESOURCE_TYPE_CSS
                RumResourceType.Media => 10, // DD_RUM_RESOURCE_TYPE_MEDIA
                RumResourceType.Other => 11, // DD_RUM_RESOURCE_TYPE_OTHER
                _ => 0, // DD_RUM_RESOURCE_TYPE_UNKNOWN
            };

        private static int MapErrorSource(RumErrorSource source) =>
            source switch
            {
                RumErrorSource.Network => 0, // DD_RUM_ERROR_SOURCE_NETWORK
                RumErrorSource.Source => 1, // DD_RUM_ERROR_SOURCE_SOURCE
                RumErrorSource.Console => 2, // DD_RUM_ERROR_SOURCE_CONSOLE
                RumErrorSource.WebView => 5, // DD_RUM_ERROR_SOURCE_WEBVIEW
                RumErrorSource.Custom => 6, // DD_RUM_ERROR_SOURCE_CUSTOM
                _ => 6,
            };

        // === P/Invoke: RUM ===

        [DllImport("dd_native")]
        private static extern void dd_rum_stop_session(IntPtr rum);

        [DllImport("dd_native")]
        private static extern void dd_rum_start_view_obj(IntPtr rum, [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref DdAttribute attributes);

        [DllImport("dd_native")]
        private static extern void dd_rum_stop_view_obj(IntPtr rum, [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            ref DdAttribute attributes);

        [DllImport("dd_native")]
        private static extern void dd_rum_add_action(IntPtr rum, int type,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref DdAttribute attributes);

        [DllImport("dd_native")]
        private static extern void dd_rum_start_action(IntPtr rum, int type,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref DdAttribute attributes);

        [DllImport("dd_native")]
        private static extern void dd_rum_stop_action(IntPtr rum, int type,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref DdAttribute attributes);

        [DllImport("dd_native")]
        private static extern void dd_rum_add_error(IntPtr rum, int source,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string message, [MarshalAs(UnmanagedType.LPUTF8Str)] string type,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string stackTrace, ref DdAttribute attributes);

        [DllImport("dd_native")]
        private static extern void dd_rum_start_resource(IntPtr rum, [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            int method, [MarshalAs(UnmanagedType.LPUTF8Str)] string url, ref DdAttribute attributes);

        [DllImport("dd_native")]
        private static extern void dd_rum_stop_resource(IntPtr rum, [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            int statusCode, long size, int type, ref DdAttribute attributes);

        [DllImport("dd_native")]
        private static extern void dd_rum_stop_resource_with_error(IntPtr rum,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string errorMessage,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string errorType,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string stackTrace, [MarshalAs(UnmanagedType.I1)] bool isNetworkError,
            int statusCode, ref DdAttribute attributes);

        [DllImport("dd_native")]
        private static extern void dd_rum_add_attribute(IntPtr rum, [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
            ref DdAttribute value);

        [DllImport("dd_native")]
        private static extern void dd_rum_remove_attribute(IntPtr rum, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    }
}
