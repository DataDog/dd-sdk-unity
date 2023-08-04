// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Datadog.Unity.Logs;
using Datadog.Unity.NativeCore.Models;
using UnityEngine;

namespace Datadog.Unity.NativeCore
{
    internal class DatadogNativeLogger : IDdLogger
    {
        private DatadogNativeCorePlatform _core;
        private DatadogLoggingOptions _configuration;

        private Dictionary<string, object> _attributes = new ();
        private HashSet<string> _tags = new ();

        public DatadogNativeLogger(DatadogNativeCorePlatform core, DatadogLoggingOptions options)
        {
            _core = core;
            _configuration = options;
        }

        public override void Log(DdLogLevel level, string message, Dictionary<string, object> attributes = null, Exception error = null)
        {
            LogEvent.ErrorInfo errorInfo = null;
            if (error != null)
            {
                errorInfo = new LogEvent.ErrorInfo()
                {
                    Message = error.Message,
                    Kind = error.GetType()?.ToString(),
                    Stack = error.StackTrace,
                };
            }

            var logEvent = new LogEvent()
            {
                Date = DateTime.Now,
                Status = level.ToString().ToLower(),
                Message = message,
                ServiceName = _configuration.ServiceName,
                Error = errorInfo,
                Logger = new ()
                {
                    Name = _configuration.LoggerName,
                    Version = "0.8.0",
                    ThreadName = Thread.CurrentThread.Name,
                },
                Version = "0.0.8",
                Tags = string.Join(',', _tags),
            };
            var encoded = logEvent.Serialize();
            _core.SendMessage(new CoreMessage("logs", new (), encoded));
        }

        public override void AddTag(string tag, string value = null)
        {
            if (value != null)
            {
                _tags.Add($"{tag}:{value}");
            }
            else
            {
                _tags.Add(tag);
            }
        }

        public override void RemoveTag(string tag)
        {
            _tags.Remove(tag);
        }

        public override void RemoveTagsWithKey(string key)
        {
            _tags.RemoveWhere(s => s.StartsWith($"{key}:"));
        }

        public override void AddAttribute(string key, object value)
        {
            _attributes[key] = value;
        }

        public override void RemoveAttribute(string key)
        {
            _attributes.Remove(key);
        }
    }
}
