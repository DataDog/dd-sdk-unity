// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Datadog.Unity.Worker;
using UnityEngine.Pool;

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
            public DateTime? MessageTime { get; protected set; }

            public string FeatureTarget => DdRumProcessor.RumTargetName;

            public abstract void Discard();
        }

        internal class StartViewMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<StartViewMessage> _pool = new (
                createFunc: () => new StartViewMessage(), actionOnRelease: (obj) => obj.Reset());

            private StartViewMessage()
            {
            }

            public string Key { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public static StartViewMessage Create(DateTime messageTime, string key, string name, Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.MessageTime = messageTime;
                obj.Key = key;
                obj.Name = name;
                obj.Attributes = attributes ?? new();

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                MessageTime = null;
                Key = null;
                Name = null;
                Attributes = null;
            }
        }

        internal class StopViewMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<StopViewMessage> _pool = new (
                createFunc: () => new StopViewMessage(), actionOnRelease: (obj) => obj.Reset());

            private StopViewMessage()
            {
            }

            public string Key { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public static StopViewMessage Create(DateTime messageTime, string key, Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.MessageTime = messageTime;
                obj.Key = key;
                obj.Attributes = attributes ?? new();

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                MessageTime = null;
                Key = null;
                Attributes = null;
            }
        }

        internal class AddUserActionMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<AddUserActionMessage> _pool = new (
                createFunc: () => new AddUserActionMessage(), actionOnRelease: (obj) => obj.Reset());

            private AddUserActionMessage()
            {
            }

            public RumUserActionType Type { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public static AddUserActionMessage Create(DateTime messageTime, RumUserActionType type, string name, Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.MessageTime = messageTime;
                obj.Type = type;
                obj.Name = name;
                obj.Attributes = attributes ?? new();

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                MessageTime = null;
                Name = null;
                Attributes = null;
            }
        }

        internal class StartUserActionMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<StartUserActionMessage> _pool = new (
                createFunc: () => new StartUserActionMessage(), actionOnRelease: (obj) => obj.Reset());

            private StartUserActionMessage()
            {
            }

            public RumUserActionType Type { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public static StartUserActionMessage Create(DateTime messageTime, RumUserActionType type, string name, Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.MessageTime = messageTime;
                obj.Type = type;
                obj.Name = name;
                obj.Attributes = attributes ?? new();

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                MessageTime = null;
                Name = null;
                Attributes = null;
            }
        }

        internal class StopUserActionMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<StopUserActionMessage> _pool = new (
                createFunc: () => new StopUserActionMessage(), actionOnRelease: (obj) => obj.Reset());

            private StopUserActionMessage()
            {
            }

            public RumUserActionType Type { get; private set; }

            public string Name { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public static StopUserActionMessage Create(DateTime messageTime, RumUserActionType type, string name, Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.MessageTime = messageTime;
                obj.Type = type;
                obj.Name = name;
                obj.Attributes = attributes ?? new();

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                MessageTime = null;
                Name = null;
                Attributes = null;
            }
        }

        internal class AddErrorMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<AddErrorMessage> _pool = new (
                createFunc: () => new AddErrorMessage(), actionOnRelease: (obj) => obj.Reset());

            private AddErrorMessage()
            {
            }

            public Exception Error { get; private set; }

            public RumErrorSource Source { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public static AddErrorMessage Create(DateTime messageTime, Exception error, RumErrorSource source, Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.MessageTime = messageTime;
                obj.Error = error;
                obj.Source = source;
                obj.Attributes = attributes ?? new();

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                MessageTime = null;
                Error = null;
                Source = default;
                Attributes = null;
            }
        }

        internal class AddAttributeMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<AddAttributeMessage> _pool = new (
                createFunc: () => new AddAttributeMessage(), actionOnRelease: (obj) => obj.Reset());

            private AddAttributeMessage()
            {
            }

            public string Key { get; private set; }

            public object Value { get; private set; }

            public static AddAttributeMessage Create(string key, object value)
            {
                var obj = _pool.Get();
                obj.Key = key;
                obj.Value = value;

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Key = null;
                Value = null;
            }
        }

        internal class RemoveAttributeMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<RemoveAttributeMessage> _pool = new (createFunc: () => new RemoveAttributeMessage());

            private RemoveAttributeMessage()
            {
            }

            public string Key { get; private set; }

            public static RemoveAttributeMessage Create(string key)
            {
                var obj = _pool.Get();
                obj.Key = key;

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }
        }

        internal class StartResourceLoadingMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<StartResourceLoadingMessage> _pool = new (
                createFunc: () => new StartResourceLoadingMessage(), actionOnRelease: (obj) => obj.Reset());

            private StartResourceLoadingMessage()
            {
            }

            public string Key { get; private set; }

            public RumHttpMethod HttpMethod { get; private set; }

            public string Url { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public static StartResourceLoadingMessage Create(DateTime messageTime, string key, RumHttpMethod httpMethod, string url, Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.MessageTime = messageTime;
                obj.Key = key;
                obj.HttpMethod = httpMethod;
                obj.Url = url;
                obj.Attributes = attributes ?? new();

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                MessageTime = null;
                Key = null;
                Url = null;
                Attributes = null;
            }
        }

        internal class StopResourceLoadingMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<StopResourceLoadingMessage> _pool = new (
                createFunc: () => new StopResourceLoadingMessage(), actionOnRelease: (obj) => obj.Reset());

            private StopResourceLoadingMessage()
            {
            }

            public string Key { get; private set; }

            public RumResourceType ResourceType { get; private set; }

            public long? Size { get; private set; }

            public int? StatusCode { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public static StopResourceLoadingMessage Create(DateTime messageTime, string key, RumResourceType resourceType, int? statusCode, long? size, Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.MessageTime = messageTime;
                obj.Key = key;
                obj.ResourceType = resourceType;
                obj.StatusCode = statusCode;
                obj.Size = size;
                obj.Attributes = attributes ?? new();

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                MessageTime = null;
                Key = null;
                Size = null;
                StatusCode = null;
                Attributes = null;
            }
        }

        internal class StopResourceLoadingWithErrorMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<StopResourceLoadingWithErrorMessage> _pool = new (
                createFunc: () => new StopResourceLoadingWithErrorMessage(), actionOnRelease: (obj) => obj.Reset());

            private StopResourceLoadingWithErrorMessage()
            {
            }

            public string Key { get; private set; }

            public RumResourceType ResourceType { get; private set; }

            public string ErrorType { get; private set; }

            public string ErrorMessage { get; private set; }

            public Dictionary<string, object> Attributes { get; private set; }

            public static StopResourceLoadingWithErrorMessage Create(DateTime messageTime, string key,
                string errorType, string errorMessage, Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.MessageTime = messageTime;
                obj.Key = key;
                obj.ErrorType = errorType;
                obj.ErrorMessage = errorMessage;
                obj.Attributes = attributes ?? new();

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                MessageTime = null;
                Key = null;
                ErrorType = null;
                ErrorMessage = null;
                Attributes = null;
            }
        }

        internal class AddFeatureFlagEvaluationMessage : DdRumWorkerMessage
        {
            private static readonly ThreadSafeObjectPool<AddFeatureFlagEvaluationMessage> _pool = new (
                createFunc: () => new AddFeatureFlagEvaluationMessage(), actionOnRelease: (obj) => obj.Reset());

            private AddFeatureFlagEvaluationMessage()
            {
            }

            public string Key { get; private set; }

            public object Value { get; private set; }

            public static AddFeatureFlagEvaluationMessage Create(string key, object value)
            {
                var obj = _pool.Get();
                obj.Key = key;
                obj.Value = value;

                return obj;
            }

            public override void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Key = null;
                Value = null;
            }
        }

        internal class StopSessionMessage : DdRumWorkerMessage
        {
            public StopSessionMessage()
            {
            }

            public override void Discard()
            {
                // This should be a fairly rare message, so we don't need to pool it.
            }
        }

        #endregion
    }
}
