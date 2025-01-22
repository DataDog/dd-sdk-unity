// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Worker;

namespace Datadog.Unity.Logs
{
    // Feeds calls to DdLogger through the DatadogWorker to be called on a background thread instead.
    internal class DdWorkerProxyLogger : DdLogger
    {
        private readonly DatadogWorker _worker;
        private readonly DdLogger _logger;

        public DdWorkerProxyLogger(DatadogWorker worker, DdLogger logger)
            : base(DdLogLevel.Debug, 100.0f) // Prevent double sampling
        {
            _worker = worker;
            _logger = logger;
        }

        public override void AddAttribute(string key, object value)
        {
            if (key == null)
            {
                LogNullWarning("AddAttribute", "key");
                return;
            }

            if (value == null)
            {
                LogNullWarning("AddAttribute", "value");
                return;
            }

            _worker.AddMessage(DdLogsProcessor.AddAttributeMessage.Create(_logger, key, value));
        }

        public override void AddTag(string tag, string value = null)
        {
            if (tag == null)
            {
                LogNullWarning("AddTag", "tag");
                return;
            }

            _worker.AddMessage(DdLogsProcessor.AddTagMessage.Create(_logger, tag, value));
        }

        public override void RemoveAttribute(string key)
        {
            if (key == null)
            {
                LogNullWarning("RemoveAttribute", "key");
                return;
            }

            _worker.AddMessage(DdLogsProcessor.RemoveAttributeMessage.Create(_logger, key));
        }

        public override void RemoveTag(string tag)
        {
            if (tag == null)
            {
                LogNullWarning("RemoveTag", "tag");
                return;
            }

            _worker.AddMessage(DdLogsProcessor.RemoveTagMessage.Create(_logger, tag));
        }

        public override void RemoveTagsWithKey(string key)
        {
            if (key == null)
            {
                LogNullWarning("RemoveTagsWithKey", "key");
                return;
            }

            _worker.AddMessage(DdLogsProcessor.RemoveTagsWithKeyMessage.Create(_logger, key));
        }

        internal override void PlatformLog(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            if (message == null)
            {
                LogNullWarning("PlatformLog", "message");
                return;
            }

            _worker.AddMessage(DdLogsProcessor.LogMessage.Create(_logger, level, message, attributes, error));
        }

        private void LogNullWarning(string methodName, string parameter)
        {
            UnityEngine.Debug.LogWarning($"[Datadog] {methodName} called with null parameter: {parameter}.");
        }
    }
}
