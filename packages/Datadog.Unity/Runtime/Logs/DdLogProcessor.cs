// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Worker;

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

        #region Messgages

        internal class LogMessage : IDatadogWorkerMessage
        {
            public LogMessage(IDdLogger logger, DdLogLevel level, string message, Dictionary<string, object> attributes, Exception error)
            {
                Logger = logger;
                Level = level;
                Message = message;
                Attributes = attributes;
                Error = error;
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public IDdLogger Logger { get; private set; }

            public DdLogLevel Level { get; private set; }

            public string Message { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public Exception Error { get; private set; }
        }

        internal class AddTagMessage : IDatadogWorkerMessage
        {
            public AddTagMessage(IDdLogger logger, string tag, string value)
            {
                Logger = logger;
                Tag = tag;
                Value = value;
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public IDdLogger Logger { get; private set; }

            public string Tag { get; private set; }

            public string Value { get; private set; }
        }

        internal class RemoveTagMessage : IDatadogWorkerMessage
        {
            public RemoveTagMessage(IDdLogger logger, string tag)
            {
                Logger = logger;
                Tag = tag;
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public IDdLogger Logger { get; private set; }

            public string Tag { get; private set; }
        }

        internal class RemoveTagsWithKeyMessage : IDatadogWorkerMessage
        {
            public RemoveTagsWithKeyMessage(IDdLogger logger, string tag)
            {
                Logger = logger;
                Tag = tag;
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public IDdLogger Logger { get; private set; }

            public string Tag { get; private set; }
        }

        internal class AddAttributeMessage : IDatadogWorkerMessage
        {
            public AddAttributeMessage(IDdLogger logger, string key, object value)
            {
                Logger = logger;
                Key = key;
                Value = value;
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public IDdLogger Logger { get; private set; }

            public string Key { get; private set; }

            public object Value { get; private set; }
        }

        internal class RemoveAttributeMessage : IDatadogWorkerMessage
        {
            public RemoveAttributeMessage(IDdLogger logger, string key)
            {
                Logger = logger;
                Key = key;
            }

            public string FeatureTarget => DdLogsProcessor.LogsTargetName;

            public IDdLogger Logger { get; private set; }

            public string Key { get; private set; }
        }

        #endregion
    }
}
