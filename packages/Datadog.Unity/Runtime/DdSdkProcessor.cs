// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2024-Present Datadog, Inc.

using System.Collections.Generic;

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
                throw new System.NotImplementedException();
            }
        }
    }
}
