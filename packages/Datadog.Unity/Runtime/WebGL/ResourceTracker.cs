// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Rum;
using UnityEngine;

namespace Datadog.Unity.WebGL
{
    /// <summary>
    /// Tracks resource start and stop calls for the Web platform, to properly
    /// send resource events to the Browser SDK.
    /// </summary>
    public class ResourceTracker
    {
        private readonly Dictionary<string, ResourceInfo> _activeResources = new ();

        public void StartResource(DateTime timestamp, string key, RumHttpMethod httpMethod, string url, Dictionary<string, object> attributes)
        {
            if (_activeResources.ContainsKey(key))
            {
                return;
            }

            var resourceInfo = new ResourceInfo(timestamp, key, httpMethod, url, attributes);
            _activeResources.Add(key, resourceInfo);
        }

        public ResourceInfo StopResource(
            DateTime timestamp,
            string key,
            RumResourceType kind,
            int? statusCode = null,
            long? size = null,
            Dictionary<string, object> attributes = null)
        {
            if (!_activeResources.ContainsKey(key))
            {
                return null;
            }

            var resourceInfo = _activeResources[key];
            _activeResources.Remove(key);
            resourceInfo.Stop(timestamp, kind, statusCode, size, attributes);

            return resourceInfo;
        }

        public ResourceInfo StopResourceWithError(
            DateTime timestamp,
            string key,
            string errorType,
            string errorMessage,
            Dictionary<string, object> attributes = null)
        {
            if (!_activeResources.ContainsKey(key))
            {
                return null;
            }

            var resourceInfo = _activeResources[key];
            _activeResources.Remove(key);
            resourceInfo.StopWithError(timestamp, errorType, errorMessage, attributes);

            return resourceInfo;
        }

        public class ResourceInfo
        {
            public DateTime StartTimestamp { get; private set; }

            public DateTime? StopTimestamp { get; private set; }

            public string Key { get; private set; }

            public RumHttpMethod Method { get; private set; }

            public string Url { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public RumResourceType Kind { get; private set; } = RumResourceType.Other;

            public int? StatusCode { get; private set; }

            public long? Size { get; private set; }

            public string ErrorType { get; private set; }

            public string ErrorMessage { get; private set; }

            public ResourceInfo(
                DateTime timestamp,
                string key,
                RumHttpMethod rumHttpMethod,
                string url,
                Dictionary<string, object> attributes)
            {
                StartTimestamp = timestamp;
                Key = key;
                Method = rumHttpMethod;
                Url = url;
                Attributes = attributes ?? new Dictionary<string, object>();
            }

            public void Stop(
                DateTime timestamp,
                RumResourceType kind,
                int? statusCode = null,
                long? size = null,
                Dictionary<string, object> attributes = null)
            {
                StopTimestamp = timestamp;
                Kind = kind;
                StatusCode = statusCode;
                Size = size;
                if (attributes != null)
                {
                    MergeAttributes(attributes);
                }
            }

            public void StopWithError(
                DateTime timestamp,
                string errorType,
                string errorMessage,
                Dictionary<string, object> attributes = null)
            {
                StopTimestamp = timestamp;
                ErrorType = errorType;
                ErrorMessage = errorMessage;
                if (attributes != null)
                {
                    MergeAttributes(attributes);
                }
            }

            private void MergeAttributes(Dictionary<string, object> source)
            {
                foreach (var kvp in source)
                {
                    Attributes[kvp.Key] = kvp.Value;
                }
            }
        }
    }
}
