// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2025-Present Datadog, Inc.

namespace Datadog.Unity.Worker
{
    internal class NoOpWorker : DatadogWorker
    {
        public override void Start()
        {
            // No operation
        }

        public override void Stop()
        {
            // No operation
        }

        public override void AddMessage(IDatadogWorkerMessage message)
        {
            // No operation
        }
    }
}
