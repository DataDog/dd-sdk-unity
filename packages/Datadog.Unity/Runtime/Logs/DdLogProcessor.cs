// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Rum;
using Datadog.Unity.Worker;
using UnityEngine.Pool;

namespace Datadog.Unity.Logs
{
    internal class DdLogsProcessor : IDatadogWorkerProcessor
    {
        public const string LogsTargetName = "logs";

        public void Process(IDatadogWorkerMessage message)
        {
            switch (message)
            {
                case LogMessage msg:
                    msg.Logger.Log(msg.Level, msg.Message, msg.Attributes, msg.Error);
                    break;
                case AddTagMessage msg:
                    msg.Logger.AddTag(msg.Tag, msg.Value);
                    break;
                case RemoveTagMessage msg:
                    msg.Logger.RemoveTag(msg.Tag);
                    break;
                case RemoveTagsWithKeyMessage msg:
                    msg.Logger.RemoveTagsWithKey(msg.Tag);
                    break;
                case AddAttributeMessage msg:
                    msg.Logger.AddAttribute(msg.Key, msg.Value);
                    break;
                case RemoveAttributeMessage msg:
                    msg.Logger.RemoveAttribute(msg.Key);
                    break;
            }
        }

        #region Messages

        internal class LogMessage : IDatadogWorkerMessage
        {
            private static ObjectPool<LogMessage> _pool = new (
                createFunc: () => new LogMessage(), actionOnRelease: (obj) => obj.Reset());

            private LogMessage()
            {
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public DdLogger Logger { get; private set; }

            public DdLogLevel Level { get; private set; }

            public string Message { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public Exception Error { get; private set; }

            public static LogMessage Create(DdLogger logger, DdLogLevel level, string message, Dictionary<string, object> attributes, Exception error)
            {
                var obj = _pool.Get();
                obj.Logger = logger;
                obj.Level = level;
                obj.Message = message;
                obj.Attributes = attributes;
                obj.Error = error;
                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Logger = null;
                Message = null;
                Attributes = null;
                Error = null;
            }
        }

        internal class AddTagMessage : IDatadogWorkerMessage
        {
            private static ObjectPool<AddTagMessage> _pool = new (
                createFunc: () => new AddTagMessage(), actionOnRelease: (obj) => obj.Reset());

            private AddTagMessage()
            {
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public DdLogger Logger { get; private set; }

            public string Tag { get; private set; }

            public string Value { get; private set; }

            public static AddTagMessage Create(DdLogger logger, string tag, string value)
            {
                var obj = _pool.Get();
                obj.Logger = logger;
                obj.Tag = tag;
                obj.Value = value;
                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Logger = null;
                Tag = null;
                Value = null;
            }
        }

        internal class RemoveTagMessage : IDatadogWorkerMessage
        {
            private static ObjectPool<RemoveTagMessage> _pool = new (
                createFunc: () => new RemoveTagMessage(), actionOnRelease: (obj) => obj.Reset());

            private RemoveTagMessage()
            {
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public DdLogger Logger { get; private set; }

            public string Tag { get; private set; }

            public static RemoveTagMessage Create(DdLogger logger, string tag)
            {
                var obj = _pool.Get();
                obj.Logger = logger;
                obj.Tag = tag;
                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Logger = null;
                Tag = null;
            }
        }

        internal class RemoveTagsWithKeyMessage : IDatadogWorkerMessage
        {
            private static ObjectPool<RemoveTagsWithKeyMessage> _pool = new (
                createFunc: () => new RemoveTagsWithKeyMessage(), actionOnRelease: (obj) => obj.Reset());

            private RemoveTagsWithKeyMessage()
            {
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public DdLogger Logger { get; private set; }

            public string Tag { get; private set; }

            public static RemoveTagsWithKeyMessage Create(DdLogger logger, string tag)
            {
                var obj = _pool.Get();
                obj.Logger = logger;
                obj.Tag = tag;
                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Logger = null;
                Tag = null;
            }
        }

        internal class AddAttributeMessage : IDatadogWorkerMessage
        {
            private static ObjectPool<AddAttributeMessage> _pool = new (
                createFunc: () => new AddAttributeMessage(), actionOnRelease: (obj) => obj.Reset());

            private AddAttributeMessage()
            {
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public DdLogger Logger { get; private set; }

            public string Key { get; private set; }

            public object Value { get; private set; }

            public static AddAttributeMessage Create(DdLogger logger, string key, object value)
            {
                var obj = _pool.Get();
                obj.Logger = logger;
                obj.Key = key;
                obj.Value = value;
                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Logger = null;
                Key = null;
                Value = null;
            }
        }

        internal class RemoveAttributeMessage : IDatadogWorkerMessage
        {
            private static ObjectPool<RemoveAttributeMessage> _pool = new (
                createFunc: () => new RemoveAttributeMessage(), actionOnRelease: (obj) => obj.Reset());

            private RemoveAttributeMessage()
            {
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public DdLogger Logger { get; private set; }

            public string Key { get; private set; }

            public static RemoveAttributeMessage Create(DdLogger logger, string key)
            {
                var obj = _pool.Get();
                obj.Logger = logger;
                obj.Key = key;
                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Logger = null;
                Key = null;
            }
        }

        #endregion
    }
}
