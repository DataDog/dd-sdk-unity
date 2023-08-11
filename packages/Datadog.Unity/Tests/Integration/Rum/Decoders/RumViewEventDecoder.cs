// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
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

        public Dictionary<string, long> CustomTimings
        {
            get
            {
                var timings = rumEvent["view"]["custom_timings"];
                var timingDict = new Dictionary<string, long>();
                foreach (var timing in timings)
                {
                    var property = (JProperty)timing;
                    timingDict.Add(property.Name, property.Value.Value<long>());
                }

                return timingDict;
            }
        }

        public RumViewEventDecoder(JObject rawJson)
            : base(rawJson)
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
        public readonly JObject viewData;

        public string Id { get => viewData["id"].Value<string>(); }

        public string Name { get => viewData["name"].Value<string>(); }

        public string Path { get => viewData["url"].Value<string>(); }

        public RumViewInfoDecoder(JObject viewData)
        {
            this.viewData = viewData;
        }
    }
}
