// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Worker;

namespace Datadog.Unity.Rum
{
    internal class DdRumProcessor : IDatadogWorkerProcessor
    {
        public const string RumTargetName = "rum";

        private readonly IDdRum _rum;

        public DdRumProcessor(IDdRum rum)
        {
            _rum = rum;
        }

        public void Process(IDatadogWorkerMessage message)
        {
            switch (message)
            {
                case StartViewMessage msg:
                    _rum.StartView(msg.Key, msg.Name, msg.Attributes);
                    break;
                case StopViewMessage msg:
                    _rum.StopView(msg.Key, msg.Attributes);
                    break;
                case AddTimingMessage msg:
                    _rum.AddTiming(msg.Name);
                    break;
                case AddUserActionMessage msg:
                    _rum.AddUserAction(msg.Type, msg.Name, msg.Attributes);
                    break;
                case StartUserActionMessage msg:
                    _rum.StartUserAction(msg.Type, msg.Name, msg.Attributes);
                    break;
                case StopUserActionMessage msg:
                    _rum.StopUserAction(msg.Type, msg.Name, msg.Attributes);
                    break;
                case AddErrorMessage msg:
                    _rum.AddError(msg.Error, msg.Source, msg.Attributes);
                    break;
            }
        }

        #region Messages

        internal class StartViewMessage : IDatadogWorkerMessage
        {
            public StartViewMessage(string key, string name, Dictionary<string, object> attributes)
            {
                Key = key;
                Name = name;
                Attributes = attributes;
            }

            public string Key { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public string FeatureTarget => RumTargetName;
        }

        internal class StopViewMessage : IDatadogWorkerMessage
        {
            public StopViewMessage(string key, Dictionary<string, object> attributes)
            {
                Key = key;
                Attributes = attributes;
            }

            public string Key { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public string FeatureTarget => RumTargetName;
        }

        internal class AddTimingMessage : IDatadogWorkerMessage
        {
            public AddTimingMessage(string name)
            {
                Name = name;
            }

            public string Name { get; private set; }

            public string FeatureTarget => RumTargetName;
        }

        internal class AddUserActionMessage : IDatadogWorkerMessage
        {
            public AddUserActionMessage(RumUserActionType type, string name, Dictionary<string, object> attributes)
            {
                Type = type;
                Name = name;
                Attributes = attributes;
            }

            public RumUserActionType Type { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public string FeatureTarget => RumTargetName;
        }

        internal class StartUserActionMessage : IDatadogWorkerMessage
        {
            public StartUserActionMessage(RumUserActionType type, string name, Dictionary<string, object> attributes)
            {
                Type = type;
                Name = name;
                Attributes = attributes;
            }

            public RumUserActionType Type { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public string FeatureTarget => RumTargetName;
        }

        internal class StopUserActionMessage : IDatadogWorkerMessage
        {
            public StopUserActionMessage(RumUserActionType type, string name, Dictionary<string, object> attributes)
            {
                Type = type;
                Name = name;
                Attributes = attributes;
            }

            public RumUserActionType Type { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public string FeatureTarget => RumTargetName;
        }

        internal class AddErrorMessage : IDatadogWorkerMessage
        {
            public AddErrorMessage(Exception error, RumErrorSource source, Dictionary<string, object> attributes)
            {
                Error = error;
                Source = source;
                Attributes = attributes;
            }

            public Exception Error { get; private set; }

            public RumErrorSource Source { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public string FeatureTarget => RumTargetName;
        }

        internal class AddAttributeMessage : IDatadogWorkerMessage
        {
            public AddAttributeMessage(string key, object value)
            {
                Key = key;
                Value = value;
            }

            public string Key { get; private set; }

            public object Value { get; private set; }

            public string FeatureTarget => RumTargetName;
        }

        internal class RemoveAttributeMessage : IDatadogWorkerMessage
        {
            public RemoveAttributeMessage(string key)
            {
                Key = key;
            }

            public string Key { get; private set; }

            public string FeatureTarget => RumTargetName;
        }

        #endregion
    }
}
