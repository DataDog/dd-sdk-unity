// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System;

namespace Datadog.Unity.Rum
{
    public interface IDateProvider
    {
        public DateTime Now { get; }
    }

    public class DefaultDateProvider : IDateProvider
    {
        public DateTime Now => DateTime.Now;
    }
}
