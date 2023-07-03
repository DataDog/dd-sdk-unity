// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;

namespace Datadog.Unity.Logs
{
    public abstract class IDdLogger
    {
        public void Debug(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Debug, message, attributes, error);
        }

        public void Info(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Info, message, attributes, error);
        }

        public void Notice(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Notice, message, attributes, error);
        }

        public void Warn(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Warn, message, attributes, error);
        }

        public void Error(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Error, message, attributes, error);
        }

        public void Critical(string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            Log(DdLogLevel.Critical, message, attributes, error);
        }

        public abstract void Log(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null);

        public abstract void AddTag(string tag, string value = null);

        public abstract void RemoveTag(string tag);

        public abstract void RemoveTagsWithKey(string key);

        public abstract void AddAttribute(string key, object value);

        public abstract void RemoveAttribute(string key);
    }

    internal class DdNoopLogger : IDdLogger
    {
        public override void AddAttribute(string key, object value)
        {
        }

        public override void AddTag(string tag, string value = null)
        {
        }

        public override void Log(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
        }

        public override void RemoveAttribute(string key)
        {
        }

        public override void RemoveTag(string tag)
        {
        }

        public override void RemoveTagsWithKey(string key)
        {
        }
    }
}
