// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System.Collections.Generic;
using Datadog.Unity.Logs;
using UnityEngine.Pool;

namespace Datadog.Unity.Worker
{
    internal class DdSdkProcessor : IDatadogWorkerProcessor
    {
        public const string SdkTargetName = "core_sdk";

        private readonly IDatadogPlatform _platform;

        internal DdSdkProcessor(IDatadogPlatform platform)
        {
            _platform = platform;
        }

        public void Process(IDatadogWorkerMessage message)
        {
            switch (message)
            {
                case SetUserInfoMessage msg:
                    _platform.SetUserInfo(msg.Id, msg.Name, msg.Email, msg.ExtraInfo);
                    break;
                case AddUserExtraInfoMessage msg:
                    _platform.AddUserExtraInfo(msg.ExtraInfo);
                    break;
                case AddGlobalAttributesMessage msg:
                    _platform.AddLogsAttributes(msg.Attributes);
                    break;
                case RemoveGlobalAttributeMessage msg:
                    _platform.RemoveLogsAttribute(msg.Key);
                    break;
                default:
                    break;
            }
        }

        internal class SetUserInfoMessage : IDatadogWorkerMessage
        {
            public string FeatureTarget => SdkTargetName;

            public string Id { get; }

            public string Name { get; }

            public string Email { get; }

            public Dictionary<string, object> ExtraInfo { get; }

            public SetUserInfoMessage(string id, string name, string email, Dictionary<string, object> extraInfo)
            {
                Id = id;
                Name = name;
                Email = email;
                ExtraInfo = extraInfo;
            }

            public void Discard()
            {
                // Nothing to do here
            }
        }

        internal class AddUserExtraInfoMessage : IDatadogWorkerMessage
        {
            public string FeatureTarget => SdkTargetName;

            public Dictionary<string, object> ExtraInfo { get; }

            public AddUserExtraInfoMessage(Dictionary<string, object> extraInfo)
            {
                ExtraInfo = extraInfo;
            }

            public void Discard()
            {
            }
        }

        // TODO: This should be moved from the core_sdk as it actually applies to Logs.
        internal class AddGlobalAttributesMessage : IDatadogWorkerMessage
        {
            private static ObjectPool<AddGlobalAttributesMessage> _pool = new (
                createFunc: () => new AddGlobalAttributesMessage(), actionOnRelease: (obj) => obj.Reset());

            private AddGlobalAttributesMessage()
            {
            }

            public string FeatureTarget => SdkTargetName;

            public Dictionary<string, object> Attributes { get; private set; } = new Dictionary<string, object>();

            public static AddGlobalAttributesMessage Create(Dictionary<string, object> attributes)
            {
                var obj = _pool.Get();
                obj.Attributes.Copy(attributes);
                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Attributes.Clear();
            }
        }

        internal class RemoveGlobalAttributeMessage : IDatadogWorkerMessage
        {
            private static ObjectPool<RemoveGlobalAttributeMessage> _pool = new (
                createFunc: () => new RemoveGlobalAttributeMessage(), actionOnRelease: (obj) => obj.Reset());

            private RemoveGlobalAttributeMessage()
            {
            }

            public string FeatureTarget => SdkTargetName;

            public string Key { get; private set; }

            public static RemoveGlobalAttributeMessage Create(string key)
            {
                var obj = _pool.Get();
                obj.Key = key;
                return obj;
            }

            public void Discard()
            {
                _pool.Release(this);
            }

            private void Reset()
            {
                Key = null;
            }
        }
    }
}
