// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

namespace Datadog.Unity
{
    /// <summary>
    /// An interface to wrap calls to various Datadog platforms.
    /// </summary>
    public interface IDatadogPlatform
    {
        void Init(DatadogConfigurationOptions options);

        IDdLogger CreateLogger();
    }

    public interface IDdLogger
    {
        void Log(string message);
    }
}
