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
        private const string DdRumTimestampAttribute = "_dd.timestamp";

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
                    InjectTime(msg.MessageTime, msg.Attributes);
                    _rum.StartView(msg.Key, msg.Name, msg.Attributes);
                    break;
                case StopViewMessage msg:
                    InjectTime(msg.MessageTime, msg.Attributes);
                    _rum.StopView(msg.Key, msg.Attributes);
                    break;
                case AddUserActionMessage msg:
                    InjectTime(msg.MessageTime, msg.Attributes);
                    _rum.AddAction(msg.Type, msg.Name, msg.Attributes);
                    break;
                case StartUserActionMessage msg:
                    InjectTime(msg.MessageTime, msg.Attributes);
                    _rum.StartAction(msg.Type, msg.Name, msg.Attributes);
                    break;
                case StopUserActionMessage msg:
                    InjectTime(msg.MessageTime, msg.Attributes);
                    _rum.StopAction(msg.Type, msg.Name, msg.Attributes);
                    break;
                case AddErrorMessage msg:
                    InjectTime(msg.MessageTime, msg.Attributes);
                    _rum.AddError(msg.Error, msg.Source, msg.Attributes);
                    break;
                case AddAttributeMessage msg:
                    _rum.AddAttribute(msg.Key, msg.Value);
                    break;
                case RemoveAttributeMessage msg:
                    _rum.RemoveAttribute(msg.Key);
                    break;
                case StartResourceLoadingMessage msg:
                    InjectTime(msg.MessageTime, msg.Attributes);
                    _rum.StartResource(msg.Key, msg.HttpMethod, msg.Url, msg.Attributes);
                    break;
                case StopResourceLoadingMessage msg:
                    InjectTime(msg.MessageTime, msg.Attributes);
                    _rum.StopResource(msg.Key, msg.ResourceType, msg.StatusCode, msg.Size, msg.Attributes);
                    break;
                case StopResourceLoadingWithErrorMessage msg:
                    InjectTime(msg.MessageTime, msg.Attributes);
                    _rum.StopResourceWithError(msg.Key, msg.ErrorType, msg.ErrorMessage, msg.Attributes);
                    break;
                case AddFeatureFlagEvaluationMessage msg:
                    _rum.AddFeatureFlagEvaluation(msg.Key, msg.Value);
                    break;
                case StopSessionMessage msg:
                    _rum.StopSession();
                    break;
            }
        }

        private void InjectTime(DateTime? time, Dictionary<string, object> attributes)
        {
            if (time == null)
            {
                return;
            }

            var offset = new DateTimeOffset(time.Value);
            attributes[DdRumTimestampAttribute] = offset.ToUnixTimeMilliseconds();
        }

        #region Messages

        internal abstract class DdRumWorkerMessage : IDatadogWorkerMessage
        {
            public DateTime? MessageTime { get; private set; }

            public string FeatureTarget => DdRumProcessor.RumTargetName;

            public DdRumWorkerMessage(DateTime? messageTime)
            {
                MessageTime = messageTime;
            }
        }

        internal class StartViewMessage : DdRumWorkerMessage
        {
            public StartViewMessage(DateTime messageTime, string key, string name, Dictionary<string, object> attributes)
                : base(messageTime)
            {
                Key = key;
                Name = name;
                Attributes = attributes ?? new ();
            }

            public string Key { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        internal class StopViewMessage : DdRumWorkerMessage
        {
            public StopViewMessage(DateTime messageTime, string key, Dictionary<string, object> attributes)
                : base(messageTime)
            {
                Key = key;
                Attributes = attributes ?? new ();
            }

            public string Key { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        internal class AddUserActionMessage : DdRumWorkerMessage
        {
            public AddUserActionMessage(DateTime messageTime, RumUserActionType type, string name, Dictionary<string, object> attributes)
                : base(messageTime)
            {
                Type = type;
                Name = name;
                Attributes = attributes ?? new ();
            }

            public RumUserActionType Type { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        internal class StartUserActionMessage : DdRumWorkerMessage
        {
            public StartUserActionMessage(DateTime messageTime, RumUserActionType type, string name, Dictionary<string, object> attributes)
                : base(messageTime)
            {
                Type = type;
                Name = name;
                Attributes = attributes ?? new ();
            }

            public RumUserActionType Type { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        internal class StopUserActionMessage : DdRumWorkerMessage
        {
            public StopUserActionMessage(DateTime messageTime, RumUserActionType type, string name, Dictionary<string, object> attributes)
                : base(messageTime)
            {
                Type = type;
                Name = name;
                Attributes = attributes ?? new ();
            }

            public RumUserActionType Type { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        internal class AddErrorMessage : DdRumWorkerMessage
        {
            public AddErrorMessage(DateTime messageTime, Exception error, RumErrorSource source, Dictionary<string, object> attributes)
                : base(messageTime)
            {
                Error = error;
                Source = source;
                Attributes = attributes ?? new ();
            }

            public Exception Error { get; private set; }

            public RumErrorSource Source { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        internal class AddAttributeMessage : DdRumWorkerMessage
        {
            public AddAttributeMessage(string key, object value)
                : base(null)
            {
                Key = key;
                Value = value;
            }

            public string Key { get; private set; }

            public object Value { get; private set; }
        }

        internal class RemoveAttributeMessage : DdRumWorkerMessage
        {
            public RemoveAttributeMessage(string key)
                : base(null)
            {
                Key = key;
            }

            public string Key { get; private set; }
        }

        internal class StartResourceLoadingMessage : DdRumWorkerMessage
        {
            public StartResourceLoadingMessage(DateTime messageTime, string key, RumHttpMethod httpMethod, string url, Dictionary<string, object> attributes)
                : base(messageTime)
            {
                Key = key;
                HttpMethod = httpMethod;
                Url = url;
                Attributes = attributes ?? new();
            }

            public string Key { get; private set; }

            public RumHttpMethod HttpMethod { get; private set; }

            public string Url { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        internal class StopResourceLoadingMessage : DdRumWorkerMessage
        {
            public StopResourceLoadingMessage(DateTime messageTime, string key, RumResourceType resourceType, int? statusCode, long? size, Dictionary<string, object> attributes)
                : base(messageTime)
            {
                Key = key;
                ResourceType = resourceType;
                StatusCode = statusCode;
                Size = size;
                Attributes = attributes ?? new();
            }

            public string Key { get; private set; }

            public RumResourceType ResourceType { get; private set; }

            public long? Size { get; private set; }

            public int? StatusCode { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        internal class StopResourceLoadingWithErrorMessage : DdRumWorkerMessage
        {
            public StopResourceLoadingWithErrorMessage(DateTime messageTime, string key,
                string errorType, string errorMessage, Dictionary<string, object> attributes)
                : base(messageTime)
            {
                Key = key;
                ErrorType = errorType;
                ErrorMessage = errorMessage;
                Attributes = attributes ?? new();
            }

            public string Key { get; private set; }

            public RumResourceType ResourceType { get; private set; }

            public string ErrorType { get; private set; }

            public string ErrorMessage { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }
        }

        internal class AddFeatureFlagEvaluationMessage : DdRumWorkerMessage
        {
            public AddFeatureFlagEvaluationMessage(string key, object value)
                : base(null)
            {
                Key = key;
                Value = value;
            }

            public string Key { get; private set; }

            public object Value { get; private set; }
        }

        internal class StopSessionMessage : DdRumWorkerMessage
        {
            public StopSessionMessage()
                : base(null)
            {
            }
        }

        #endregion
    }
}
