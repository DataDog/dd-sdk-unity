// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumViewVisit
    {
        public string Id { get; private set; }

        public string Name { get; private set; }

        public string Path { get; private set; }

        public List<RumViewEventDecoder> ViewEvents = new ();

        public RumViewVisit(string id, string name, string path)
        {
            Id = id;
            Name = name;
            Path = path;
        }
    }
}
