// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumViewEventDecoder : RumEventDecoder
    {
        public RumViewDecoder View { get; private set; }

        public int TimeSpent
        {
            get
            {
                return RumEventDecoder.jsonGetProp<int>(rumEvent, "view.time_spent");
            }
        }

        public RumViewEventDecoder(JObject rawJson) : base(rawJson)
        {
            View = new (rawJson.GetValue("view") as JObject);
        }
    }

    public class RumViewDecoder
    {
        public readonly JObject viewData;

        public string Id { get => viewData.Value<string>("id"); }

        public string Name { get => viewData.Value<string>("name"); }

        public string Path { get => viewData.Value<string>("path"); }

        public bool IsActive { get => viewData.Value<bool>("is_active"); }

        public int ActionCount { get => RumEventDecoder.jsonGetProp<int>(viewData, "action.count"); }

        public int ResourceCount { get => RumEventDecoder.jsonGetProp<int>(viewData, "resource.count"); }

        public int ErrorCount { get => RumEventDecoder.jsonGetProp<int>(viewData, "error.count"); }

        public int LongTaskCount { get => RumEventDecoder.jsonGetProp<int>(viewData, "long_task.count"); }

        public RumViewDecoder(JObject viewData)
        {
            this.viewData = viewData;
        }
    }

    public class RumViewInfoDecoder
    {
        public readonly Dictionary<string, object> viewData;

        public string Id { get => viewData["id"] as string; }

        public string Name { get => viewData["name"] as string; }

        public string Path { get => viewData["url"] as string; }

        public RumViewInfoDecoder(Dictionary<string, object> viewData)
        {
            this.viewData = viewData;
        }
    }
}
